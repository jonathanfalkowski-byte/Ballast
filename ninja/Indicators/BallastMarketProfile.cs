#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// BallastMarketProfile
// ---------------------------------------------------------------------------
// Original clean-room TPO / Market Profile indicator. It does NOT draw the full
// letter grid — only the few lines that actually matter:
//
//   pPOC  Prior session Point of Control  — the price where the most TIME was
//         spent (the fairest price, the biggest magnet).  Solid amber.
//   pVAH  Prior session Value Area High   — top of the range holding ~70% of
//   pVAL  Prior session Value Area Low      the session's activity.  Dashed amber.
//   dPOC  Today's developing POC          — where value is building right now.
//         Dotted amber. Optional.
//
// TPO = Time Price Opportunity: split the session into fixed brackets (30 min by
// default) and, for each price row, count how many brackets traded there. The
// count is TIME, so the profile is built from a separate 30-minute data series
// rather than the chart's bars — which means it is correct on a range-bar chart,
// not distorted by it.
//
// Prior session defaults to RTH 09:30-16:00 (CHART MUST BE SET TO ET), the level
// set the rest of the market watches. Context only — it marks WHERE, never WHEN
// or which way. Advisory, like everything in Ballast.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastMarketProfile : Indicator
	{
		private readonly Dictionary<int, int> devCounts = new Dictionary<int, int>();

		// The developing POC, tracked as the profile is built instead of found by
		// scanning it.
		//
		// MaxRow walked every row of the session's profile, and it was called
		// TWICE on every primary bar - once for the plot and once for the label.
		// On a range chart that is thousands of bars a day, and devCounts grows
		// all session as the range widens, so the cost per bar rose through the
		// day. That is the shape of the complaint: not slow from the open, slow
		// and getting slower.
		//
		// A running maximum is O(1) per update and cannot drift, because the only
		// thing that ever changes a count is AddRange.
		private int devPocRow;
		private int devPocCount = -1;

		// What each label currently says, so an unchanged one is not redrawn.
		private readonly Dictionary<string, string> drawn = new Dictionary<string, string>();

		// The last TPO bracket each price row was credited for. This is what makes
		// the count TIME rather than bar count: a row touched by forty range bars
		// inside one thirty-minute bracket still scores one.
		private readonly Dictionary<int, long> lastBracket = new Dictionary<int, long>();
		private DateTime	devDate = DateTime.MinValue;
		private bool		devSessionStarted;
		private double		pPocLevel, pVahLevel, pValLevel;
		private bool		priorValid;
		private double		rowSize;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"TPO / Market Profile — prior-session POC and value-area lines only (original, clean-room).";
				Name		= "BallastMarketProfile";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= true;
				IsSuspendedWhileInactive	= true;

				TpoPeriodMinutes	= 30;
				TicksPerRow			= 4;
				ValueAreaPercent	= 70;
				SessionStartET		= 930;	// HHmm
				SessionEndET		= 1600;	// HHmm
				ShowValueArea		= true;
				ShowDevelopingPOC	= true;
				ShowLabels			= true;

				// Amber = key levels/targets, per the Ballast visual system.
				AddPlot(new Stroke(SB("#E0A63C"), DashStyleHelper.Solid, 2), PlotStyle.Line, "Prior POC");
				AddPlot(new Stroke(SB("#E0A63C"), DashStyleHelper.Dash,  1), PlotStyle.Line, "Prior VAH");
				AddPlot(new Stroke(SB("#E0A63C"), DashStyleHelper.Dash,  1), PlotStyle.Line, "Prior VAL");
				AddPlot(new Stroke(SB("#B58A34"), DashStyleHelper.Dot,   1), PlotStyle.Line, "Developing POC");
			}
			else if (State == State.Configure)
			{
				// NO SECOND DATA SERIES. This is the whole performance fix.
				//
				// It used to add a 30-minute series so that "time at price" was
				// honest on a range chart. The intent was right and the cost was
				// ruinous: a second series means NinjaTrader builds and maintains
				// a second set of bars from the same feed, and then has to
				// interleave them with the primary by timestamp on every update.
				// On a RANGE chart, whose bars close at irregular times and can
				// close several times a second in a fast market, that
				// synchronisation is the expensive part - and it is paid on every
				// tick, all session, growing with the number of bars held.
				//
				// It is not needed. A TPO count is "how many distinct 30-minute
				// brackets did this price trade in", and a bar's own TIMESTAMP
				// says which bracket it falls in. So the brackets are derived from
				// the chart's own bars instead of from a parallel series: each
				// price row is credited once per bracket, however many bars touch
				// it. That is the same measurement - time, not bar count - and it
				// is correct on a range chart for exactly the reason the second
				// series was added.
			}
			else if (State == State.DataLoaded)
			{
				rowSize = TicksPerRow * TickSize;
				if (rowSize <= 0) rowSize = TickSize;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			// Roll on the calendar day BEFORE the session filter, not after.
			//
			// Accumulation only looks at RTH bars, so if the roll lived only in
			// there, yesterday's levels would not appear until the first bar after
			// 09:30 - and the overnight session and the open, which is exactly
			// when those levels are being traded against, would still be showing
			// the day before's. Rolling here puts them up the instant the date
			// turns. RollIfNewDay is idempotent, so the call inside AccumulateTpo
			// costs nothing.
			RollIfNewDay(Time[0].Date);

			AccumulateTpo();

			bool haveDev = ShowDevelopingPOC && devCounts.Count > 0;
			double devLevel = haveDev ? PriceOf(devPocRow) : double.NaN;

			PriorPOC[0] = priorValid ? pPocLevel : double.NaN;
			PriorVAH[0] = (priorValid && ShowValueArea) ? pVahLevel : double.NaN;
			PriorVAL[0] = (priorValid && ShowValueArea) ? pValLevel : double.NaN;
			DevPOC[0]   = devLevel;

			if (ShowLabels)
			{
				if (priorValid)
				{
					Lbl("BMP_poc", "pPOC", pPocLevel, Plots[0].Brush);
					if (ShowValueArea)
					{
						Lbl("BMP_vah", "pVAH", pVahLevel, Plots[1].Brush);
						Lbl("BMP_val", "pVAL", pValLevel, Plots[2].Brush);
					}
				}
				if (haveDev)
					Lbl("BMP_dpoc", "dPOC", devLevel, Plots[3].Brush);
			}
		}

		// ── TPO accumulation ────────────────────────────────────────────────
		private void AccumulateTpo()
		{
			DateTime t   = Time[0];
			int      tod = t.Hour * 60 + t.Minute;
			int      s   = SessionStartET / 100 * 60 + SessionStartET % 100;
			int      e   = SessionEndET   / 100 * 60 + SessionEndET   % 100;

			// A bar is stamped at its CLOSE, so a bar closing at exactly 09:30
			// belongs to the pre-open. Keep bars closing in (start, end].
			if (tod <= s || tod > e) return;

			RollIfNewDay(t.Date);

			// Which bracket this bar closed in. Minutes since midnight divided by
			// the bracket length, made unique per day so yesterday's 10:00 bracket
			// can never be mistaken for today's.
			long bracket = t.Date.Ticks / TimeSpan.TicksPerDay * 1000L
			             + (tod / (TpoPeriodMinutes < 1 ? 1 : TpoPeriodMinutes));

			AddRange(Low[0], High[0], bracket);
		}

		// Freeze the finished session into the prior-session levels and start a
		// fresh count. Idempotent within a day, and safe to call from either
		// series — whichever sees the new date first does the work.
		private void RollIfNewDay(DateTime d)
		{
			if (devSessionStarted && d == devDate) return;

			if (devCounts.Count > 0)
			{
				ComputeProfile(devCounts, out pPocLevel, out pVahLevel, out pValLevel);
				priorValid = true;
			}
			devCounts.Clear();
			lastBracket.Clear();
			devPocRow = 0;
			devPocCount = -1;
			drawn.Clear();          // yesterday's labels must not be held as current
			devDate = d;
			devSessionStarted = true;
		}

		private void AddRange(double lo, double hi, long bracket)
		{
			int rLo = RowOf(lo), rHi = RowOf(hi);
			for (int r = rLo; r <= rHi; r++)
			{
				// Once per bracket per row. Without this a range chart would count
				// bars, and a busy thirty minutes would out-score a quiet hour -
				// which is the distortion the whole design exists to avoid.
				long seen;
				if (lastBracket.TryGetValue(r, out seen) && seen == bracket) continue;
				lastBracket[r] = bracket;

				int c;
				devCounts.TryGetValue(r, out c);
				devCounts[r] = c + 1;

				if (c + 1 > devPocCount) { devPocCount = c + 1; devPocRow = r; }
			}
		}

		// POC = most-visited row. Value area = expand out from the POC, always
		// taking the busier adjacent row, until 70% of the TPOs are enclosed.
		private void ComputeProfile(Dictionary<int, int> counts, out double poc, out double vah, out double val)
		{
			poc = vah = val = 0;
			if (counts.Count == 0) return;

			int pocRow = 0, pocCount = -1, total = 0, minRow = int.MaxValue, maxRow = int.MinValue;
			foreach (KeyValuePair<int, int> kv in counts)
			{
				total += kv.Value;
				if (kv.Value > pocCount) { pocCount = kv.Value; pocRow = kv.Key; }
				if (kv.Key < minRow) minRow = kv.Key;
				if (kv.Key > maxRow) maxRow = kv.Key;
			}

			double target = total * (ValueAreaPercent / 100.0);
			int hi = pocRow, lo = pocRow, sum = pocCount;

			while (sum < target && (lo > minRow || hi < maxRow))
			{
				int up = hi + 1, dn = lo - 1;
				bool canUp = up <= maxRow, canDn = dn >= minRow;

				int uc = 0, dc = 0;
				if (canUp) counts.TryGetValue(up, out uc);
				if (canDn) counts.TryGetValue(dn, out dc);

				if (canUp && (!canDn || uc >= dc)) { hi = up; sum += uc; }
				else if (canDn) { lo = dn; sum += dc; }
				else break;
			}

			poc = PriceOf(pocRow);
			vah = PriceOf(hi);
			val = PriceOf(lo);
		}

		private int    RowOf(double price) { return (int)Math.Floor(price / rowSize + 1e-9); }
		private double PriceOf(int row)     { return (row + 0.5) * rowSize; }

		private void Lbl(string tag, string text, double y, Brush brush)
		{
			// Not once per historical bar.
			//
			// Every Draw call goes through NinjaTrader's draw-object machinery and
			// invalidates the chart. Loading a range chart replays thousands of
			// bars, and this was drawing four labels on each of them - all but the
			// last of which were immediately overwritten by the next bar. Only the
			// tail of history is worth drawing, because that is the only part
			// still on screen when the load finishes.
			if (State == State.Historical && CurrentBar < BarsArray[0].Count - 2) return;

			string label = text + "  " + Instrument.MasterInstrument.FormatPrice(y);

			// And not again when it would say exactly the same thing in exactly
			// the same place. A prior-session POC does not move all day; the
			// developing one moves a few times an hour. The rest of the calls were
			// replacing a label with an identical copy of itself and asking the
			// chart to repaint for it - on the chart's own UI thread, once per
			// bar, on a chart whose bars close several times a second in a fast
			// market. That is the lag, and it compounds because the repaint queue
			// never catches up.
			string was;
			if (drawn.TryGetValue(tag, out was) && was == label) return;
			drawn[tag] = label;

			Draw.Text(this, tag, false, label,
				0, y, 6, brush,
				new NinjaTrader.Gui.Tools.SimpleFont("Arial", 11),
				System.Windows.TextAlignment.Left,
				Brushes.Transparent, Brushes.Black, 55);
		}

		private static Brush SB(string hex)
		{
			Brush b = (Brush)new BrushConverter().ConvertFrom(hex);
			b.Freeze();
			return b;
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "TPO bracket (minutes)", Order = 0, GroupName = "Profile")]
		public int TpoPeriodMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Ticks per row", Order = 1, GroupName = "Profile")]
		public int TicksPerRow { get; set; }

		[NinjaScriptProperty]
		[Range(50, 95)]
		[Display(Name = "Value area %", Order = 2, GroupName = "Profile")]
		public double ValueAreaPercent { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Session start (HHmm ET)", Order = 3, GroupName = "Session")]
		public int SessionStartET { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Session end (HHmm ET)", Order = 4, GroupName = "Session")]
		public int SessionEndET { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show value area (VAH/VAL)", Order = 5, GroupName = "Display")]
		public bool ShowValueArea { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show developing POC", Order = 6, GroupName = "Display")]
		public bool ShowDevelopingPOC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Order = 7, GroupName = "Display")]
		public bool ShowLabels { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> PriorPOC { get { return Values[0]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> PriorVAH { get { return Values[1]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> PriorVAL { get { return Values[2]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DevPOC { get { return Values[3]; } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastMarketProfile[] cacheBallastMarketProfile;
		public BallastMarketProfile BallastMarketProfile(int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			return BallastMarketProfile(Input, tpoPeriodMinutes, ticksPerRow, valueAreaPercent, sessionStartET, sessionEndET, showValueArea, showDevelopingPOC, showLabels);
		}

		public BallastMarketProfile BallastMarketProfile(ISeries<double> input, int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			if (cacheBallastMarketProfile != null)
				for (int idx = 0; idx < cacheBallastMarketProfile.Length; idx++)
					if (cacheBallastMarketProfile[idx] != null && cacheBallastMarketProfile[idx].TpoPeriodMinutes == tpoPeriodMinutes && cacheBallastMarketProfile[idx].TicksPerRow == ticksPerRow && cacheBallastMarketProfile[idx].ValueAreaPercent == valueAreaPercent && cacheBallastMarketProfile[idx].SessionStartET == sessionStartET && cacheBallastMarketProfile[idx].SessionEndET == sessionEndET && cacheBallastMarketProfile[idx].ShowValueArea == showValueArea && cacheBallastMarketProfile[idx].ShowDevelopingPOC == showDevelopingPOC && cacheBallastMarketProfile[idx].ShowLabels == showLabels && cacheBallastMarketProfile[idx].EqualsInput(input))
						return cacheBallastMarketProfile[idx];
			return CacheIndicator<BallastMarketProfile>(new BallastMarketProfile(){ TpoPeriodMinutes = tpoPeriodMinutes, TicksPerRow = ticksPerRow, ValueAreaPercent = valueAreaPercent, SessionStartET = sessionStartET, SessionEndET = sessionEndET, ShowValueArea = showValueArea, ShowDevelopingPOC = showDevelopingPOC, ShowLabels = showLabels }, input, ref cacheBallastMarketProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastMarketProfile BallastMarketProfile(int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			return indicator.BallastMarketProfile(Input, tpoPeriodMinutes, ticksPerRow, valueAreaPercent, sessionStartET, sessionEndET, showValueArea, showDevelopingPOC, showLabels);
		}

		public Indicators.BallastMarketProfile BallastMarketProfile(ISeries<double> input , int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			return indicator.BallastMarketProfile(input, tpoPeriodMinutes, ticksPerRow, valueAreaPercent, sessionStartET, sessionEndET, showValueArea, showDevelopingPOC, showLabels);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastMarketProfile BallastMarketProfile(int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			return indicator.BallastMarketProfile(Input, tpoPeriodMinutes, ticksPerRow, valueAreaPercent, sessionStartET, sessionEndET, showValueArea, showDevelopingPOC, showLabels);
		}

		public Indicators.BallastMarketProfile BallastMarketProfile(ISeries<double> input , int tpoPeriodMinutes, int ticksPerRow, double valueAreaPercent, int sessionStartET, int sessionEndET, bool showValueArea, bool showDevelopingPOC, bool showLabels)
		{
			return indicator.BallastMarketProfile(input, tpoPeriodMinutes, ticksPerRow, valueAreaPercent, sessionStartET, sessionEndET, showValueArea, showDevelopingPOC, showLabels);
		}
	}
}

#endregion
