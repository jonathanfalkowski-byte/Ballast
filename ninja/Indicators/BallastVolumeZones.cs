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

// BallastVolumeZones  (Ballast system palette: teal demand / red supply)
// ---------------------------------------------------------------------------
// Original range-bar volume supply/demand zones built on the DEPARTURE concept:
// a level matters because price left it fast. On range bars a departure is a run
// of consecutive same-direction bars (each bar = one fixed range of travel).
//
//   * up departure   -> the base it launched from is a DEMAND zone (teal, below)
//   * down departure -> the base is a SUPPLY zone (red, above)
//   * the base spans BaseBars bars (a small cluster) so a normal pullback tests
//     it rather than instantly breaking it -- important on tight range bars
//   * an ACTIVE zone extends to the right as a live level
//   * once price CLOSES beyond its far edge it is BROKEN: the box STOPS at the
//     break bar and recolors (it does not project forward any more)
//
// Built from the generic departure-from-base volume-S/D concept, written from
// scratch. Not derived from or a copy of any third-party product.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastVolumeZones : Indicator
	{
		private class Zone
		{
			public double Low;
			public double High;
			public int    Bar;			// base bar (left edge)
			public bool   IsSupply;
			public bool   Broken;
			public int    BrokenBar;	// bar price closed through it (right edge when broken)
			public int    BrokenSession;	// session index at the break (for the history window)
			public double Ratio;

			// Volume-at-price profile built inside the zone (Volumetric bars only).
			public double[] BandVol;	// accumulated total volume per price band
			public double   BandBase;	// price of band 0 (== Low at profile build)
			public double   BandStep;	// price height of one band
			public int      BandCount;
			public bool     ProfileInit;

			// Freshness: how far price has traded back into the zone since it formed.
			public double PenLow  = double.MaxValue;	// lowest low that re-entered (demand)
			public double PenHigh = double.MinValue;	// highest high that re-entered (supply)

			public double Center { get { return (Low + High) * 0.5; } }
			public double Height { get { return High - Low; } }
		}

		private List<Zone> zones = new List<Zone>();

		private int curDir;
		private int runLen;
		private int runStartBar;
		private int sessionIndex;		// counts trading sessions seen
		private int lastBrokenCount;	// broken-history draw objects drawn last pass

		// Set in DataLoaded: non-null only when the chart is running Volumetric
		// (Order Flow) bars, which is the only source of true volume-at-price.
		private NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType volBars;

		private static Brush SB(byte r, byte g, byte b)
		{
			SolidColorBrush br = new SolidColorBrush(Color.FromRgb(r, g, b));
			br.Freeze();
			return br;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"Range-bar volume supply/demand from departure moves; box stops at the break (original).";
				Name		= "BallastVolumeZones";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				LegBars			= 3;
				BaseBars		= 2;
				UseVolumeFilter	= false;
				VolumeLookback	= 20;
				VolumeMultiple	= 1.3;
				MaxZones		= 3;
				ExtendRightBars	= 10;
				ShowLabels		= true;
				ShowBroken		= true;
				BaseOpacity		= 20;
				HistoryDays		= 3;		// keep broken zones on screen this many sessions
				ShowVolume		= true;		// volume-at-price shading + POC line (Volumetric bars)

				SupplyBrush	= SB(0xEF, 0x53, 0x53);
				DemandBrush	= SB(0x26, 0xA6, 0x9A);
				BrokenBrush	= SB(0x60, 0x6B, 0x78);
			}
			else if (State == State.DataLoaded)
			{
				// Volume-at-price only exists on Volumetric (Order Flow) bars. On any
				// other bar type this stays null and the volume overlay is simply
				// skipped -- the zones still draw exactly as before.
				volBars = (BarsArray != null && BarsArray.Length > 0)
					? BarsArray[0].BarsType as NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType
					: null;
			}
		}

		protected override void OnBarUpdate()
		{
			// Count trading sessions so broken-zone history is aged off in days,
			// not bars (works on any timeframe or range setting).
			if (Bars.IsFirstBarOfSession && CurrentBar > 0)
				sessionIndex++;

			if (CurrentBar < 1)
				return;

			// 1) Track the current run of same-direction bars.
			int dir = Close[0] > Open[0] ? 1 : Close[0] < Open[0] ? -1 : 0;
			if (dir != 0 && dir == curDir)
				runLen++;
			else
			{
				curDir      = dir;
				runLen      = dir == 0 ? 0 : 1;
				runStartBar = CurrentBar;
			}

			// 2) A run first reaching LegBars confirms a departure -> mark the base.
			if (dir != 0 && runLen == LegBars && CurrentBar >= Math.Max(LegBars + BaseBars, VolumeLookback))
				TryCreateZone(dir);

			// 3) Break a zone the moment price closes beyond its far edge, and
			//    pin the break bar so the box ends there.
			double close = Close[0];
			foreach (Zone z in zones)
			{
				if (z.Broken)
					continue;
				bool broke = z.IsSupply ? close > z.High : close < z.Low;
				if (broke)
				{
					z.Broken        = true;
					z.BrokenBar     = CurrentBar;
					z.BrokenSession = sessionIndex;
				}
			}

			// Age broken zones off once they are older than the history window, so
			// the chart shows the last few days of broken levels and no more.
			zones.RemoveAll(z => z.Broken && sessionIndex - z.BrokenSession > HistoryDays);

			// 4) Volume-at-price: keep each live zone's in-band profile up to date and
			//    track how far price has traded back into it (freshness). Frozen once
			//    the zone breaks, so a broken zone keeps the picture it had at the break.
			if (ShowVolume && volBars != null)
			{
				foreach (Zone z in zones)
				{
					if (z.Broken)
						continue;

					if (!z.ProfileInit)
						BuildProfile(z);			// backfills base..now, including this bar
					else
					{
						AddBarToBands(z, CurrentBar);	// this newly closed bar
						UpdatePenetration(z);
					}
				}
			}

			DrawZones();
		}

		private void TryCreateZone(int dir)
		{
			int firstBaseBarsAgo = LegBars;
			int lastBaseBarsAgo  = LegBars + BaseBars - 1;

			double zLow  = double.MaxValue;
			double zHigh = double.MinValue;
			for (int b = firstBaseBarsAgo; b <= lastBaseBarsAgo; b++)
			{
				if (High[b] > zHigh) zHigh = High[b];
				if (Low[b]  < zLow)  zLow  = Low[b];
			}

			double ratio = 1.0;
			if (CurrentBar >= VolumeLookback)
			{
				double sumVol = 0;
				for (int i = 0; i < LegBars; i++)
					sumVol += Volume[i];
				double avgRun  = sumVol / LegBars;
				double baseAvg = SMA(Volume, VolumeLookback)[0];
				ratio = baseAvg > 0 ? avgRun / baseAvg : 1.0;
			}
			if (UseVolumeFilter && ratio < VolumeMultiple)
				return;

			zones.Add(new Zone
			{
				Low  = zLow,
				High = zHigh,
				Bar  = CurrentBar - lastBaseBarsAgo,
				IsSupply = dir < 0,
				Ratio = Math.Max(ratio, 1.0)
			});
		}

		private void DrawZones()
		{
			double close = Close[0];

			List<Zone> supply = new List<Zone>();
			List<Zone> demand = new List<Zone>();
			List<Zone> broken = new List<Zone>();
			foreach (Zone z in zones)
			{
				if (z.Broken)        broken.Add(z);
				else if (z.IsSupply) supply.Add(z);
				else                 demand.Add(z);
			}

			int brokenOp = Math.Max(12, BaseOpacity / 2);

			DrawList(SelectNearest(supply, close), "VZ_sup", SupplyBrush, true, 0,        "S", ShowLabels, true);
			DrawList(SelectNearest(demand, close), "VZ_dem", DemandBrush, true, 0,        "D", ShowLabels, false);
			DrawBrokenHistory(broken, brokenOp);
		}

		// Broken zones kept as history for HistoryDays sessions. Unlike the live
		// supply/demand zones this is deliberately NOT capped to the MaxZones
		// nearest price -- the whole point is to see the run of broken levels over
		// the last few days. Each box already stops at its break bar and wears the
		// broken colour; here we simply show every one still inside the window,
		// newest break first, with a hard cap so a wild session cannot spawn
		// hundreds of drawings.
		private void DrawBrokenHistory(List<Zone> broken, int op)
		{
			const int MaxBroken = 60;

			List<Zone> show = new List<Zone>();
			if (ShowBroken)
			{
				show.AddRange(broken);
				show.Sort((a, b) => b.BrokenBar.CompareTo(a.BrokenBar));	// newest break first
				if (show.Count > MaxBroken)
					show.RemoveRange(MaxBroken, show.Count - MaxBroken);
			}

			for (int i = 0; i < show.Count; i++)
			{
				Zone z = show[i];
				int startBarsAgo = CurrentBar - z.Bar;
				int endBarsAgo   = CurrentBar - z.BrokenBar;	// stop the box at the break
				if (endBarsAgo >= startBarsAgo)
					endBarsAgo = startBarsAgo - 1;

				string tag = "VZ_brk" + i;
				Draw.Rectangle(this, tag, false,
					startBarsAgo, z.High, endBarsAgo, z.Low,
					BrokenBrush, BrokenBrush, op);
				RemoveDrawObject(tag + "_t");
			}

			// Clear boxes left over from a busier previous pass.
			for (int i = show.Count; i < lastBrokenCount; i++)
			{
				RemoveDrawObject("VZ_brk" + i);
				RemoveDrawObject("VZ_brk" + i + "_t");
			}
			lastBrokenCount = show.Count;
		}

		private List<Zone> SelectNearest(List<Zone> cand, double close)
		{
			cand.Sort((a, b) => Math.Abs(a.Center - close).CompareTo(Math.Abs(b.Center - close)));
			List<Zone> chosen = new List<Zone>();
			foreach (Zone z in cand)
			{
				bool tooClose = false;
				foreach (Zone j in chosen)
					if (Math.Abs(z.Center - j.Center) < Math.Max(z.Height, j.Height))
					{ tooClose = true; break; }
				if (!tooClose)
					chosen.Add(z);
				if (chosen.Count >= MaxZones)
					break;
			}
			return chosen;
		}

		private void DrawList(List<Zone> chosen, string prefix, Brush brush,
			bool useRatio, int fixedOp, string sym, bool showLabel, bool labelTop)
		{
			for (int i = 0; i < MaxZones; i++)
			{
				string tag = prefix + i;

				if (i >= chosen.Count)
				{
					RemoveDrawObject(tag);
					RemoveDrawObject(tag + "_t");
					RemoveDrawObject(tag + "_hot");
					RemoveDrawObject(tag + "_poc");
					RemoveDrawObject(tag + "_used");
					continue;
				}

				Zone z = chosen[i];
				int  startBarsAgo = CurrentBar - z.Bar;
				// Active zones project forward; broken zones stop at the break bar.
				int  endBarsAgo   = z.Broken ? (CurrentBar - z.BrokenBar) : -ExtendRightBars;
				int  op = useRatio ? Math.Min(48, (int)Math.Round(BaseOpacity * z.Ratio)) : fixedOp;

				Draw.Rectangle(this, tag, false,
					startBarsAgo, z.High, endBarsAgo, z.Low,
					brush, brush, op);

				// Volume-at-price: shade the heavy band, line the peak, mark what has
				// been retested. Skipped (and cleared) on non-Volumetric charts.
				DrawVolumeOverlay(tag, z, brush, startBarsAgo, endBarsAgo);

				if (showLabel)
				{
					double labelY = labelTop ? z.High : z.Low;
					Draw.Text(this, tag + "_t", false,
						sym + "  " + Instrument.MasterInstrument.FormatPrice(z.Center),
						startBarsAgo, labelY, 0, brush,
						new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9),
						System.Windows.TextAlignment.Right,
						Brushes.Transparent, Brushes.Black, 45);
				}
				else
				{
					RemoveDrawObject(tag + "_t");
				}
			}
		}

		// ── Volume-at-price inside a zone (Volumetric bars only) ─────────────
		//
		// GetTotalVolumeForPrice gives the real volume traded at each price for a
		// bar, so we can build a proper little profile confined to the zone's price
		// band and accumulate it across every bar that trades there while the zone
		// is alive. Frozen at the break.

		private double VolAtPrice(int barIndex, double price)
		{
			if (volBars == null || barIndex < 0) return 0;
			try
			{
				if (volBars.Volumes == null || barIndex >= volBars.Volumes.Length) return 0;
				return volBars.Volumes[barIndex].GetTotalVolumeForPrice(price);
			}
			catch { return 0; }
		}

		private void BuildProfile(Zone z)
		{
			int nticks = (int)Math.Round((z.High - z.Low) / TickSize);
			if (nticks < 0) nticks = 0;

			// One band per tick, but coarsen if the zone is unusually tall so the
			// array (and the per-bar loop) stays bounded.
			int step = 1;
			int bands = nticks + 1;
			if (bands > 240) { step = (bands + 239) / 240; bands = (nticks / step) + 1; }

			z.BandBase  = z.Low;
			z.BandStep  = TickSize * step;
			z.BandCount = bands;
			z.BandVol   = new double[bands];
			z.PenLow    = double.MaxValue;
			z.PenHigh   = double.MinValue;

			for (int b = z.Bar; b <= CurrentBar; b++)
				AddBarToBands(z, b);

			z.ProfileInit = true;
		}

		private void AddBarToBands(Zone z, int barIndex)
		{
			if (volBars == null || z.BandVol == null || barIndex < 0) return;

			double bh = High.GetValueAt(barIndex);
			double bl = Low.GetValueAt(barIndex);
			if (bh < z.Low || bl > z.High) return;			// bar never traded in the band

			int ticksPerBand = (int)Math.Round(z.BandStep / TickSize);
			if (ticksPerBand < 1) ticksPerBand = 1;

			for (int i = 0; i < z.BandCount; i++)
			{
				double p0 = z.BandBase + i * z.BandStep;
				double v  = 0;
				for (int t = 0; t < ticksPerBand; t++)
					v += VolAtPrice(barIndex, p0 + t * TickSize);
				z.BandVol[i] += v;
			}
		}

		private void UpdatePenetration(Zone z)
		{
			double hi = High[0], lo = Low[0];
			if (hi < z.Low || lo > z.High) return;			// bar did not reach into the zone

			if (z.IsSupply)
			{
				double reached = Math.Min(hi, z.High);
				if (reached > z.PenHigh) z.PenHigh = reached;
			}
			else
			{
				double reached = Math.Max(lo, z.Low);
				if (reached < z.PenLow) z.PenLow = reached;
			}
		}

		private void DrawVolumeOverlay(string tag, Zone z, Brush brush, int startBarsAgo, int endBarsAgo)
		{
			bool off = !ShowVolume || volBars == null || z.BandVol == null || z.BandCount <= 0;

			double sum = 0, max = 0;
			int    pocIdx = 0;
			if (!off)
			{
				for (int i = 0; i < z.BandCount; i++)
				{
					sum += z.BandVol[i];
					if (z.BandVol[i] > max) { max = z.BandVol[i]; pocIdx = i; }
				}
			}

			if (off || sum <= 0 || max <= 0)
			{
				RemoveDrawObject(tag + "_hot");
				RemoveDrawObject(tag + "_poc");
				RemoveDrawObject(tag + "_used");
				return;
			}

			double avg      = sum / z.BandCount;
			double strength = avg > 0 ? max / avg : 1.0;			// how peaked the profile is

			// Hot span: the run of bands trading clearly above average. Falls back to
			// just the peak band if nothing clears the bar.
			const double hotMult = 1.5;
			int loIdx = -1, hiIdx = -1;
			for (int i = 0; i < z.BandCount; i++)
				if (z.BandVol[i] >= avg * hotMult) { if (loIdx < 0) loIdx = i; hiIdx = i; }
			if (loIdx < 0) { loIdx = hiIdx = pocIdx; }

			double hotLow  = Math.Max(z.Low,  z.BandBase + loIdx * z.BandStep - z.BandStep * 0.5);
			double hotHigh = Math.Min(z.High, z.BandBase + hiIdx * z.BandStep + z.BandStep * 0.5);

			// Stronger peak -> more opaque highlight.
			int hotOp = (int)Math.Round(18 + (strength - 1.0) * 14);
			if (hotOp < 18) hotOp = 18;
			if (hotOp > 60) hotOp = 60;

			Draw.Rectangle(this, tag + "_hot", false,
				startBarsAgo, hotHigh, endBarsAgo, hotLow, brush, brush, hotOp);

			// A line straight across the zone at the peak-volume price.
			double pocPrice = z.BandBase + pocIdx * z.BandStep;
			Draw.Line(this, tag + "_poc", false,
				startBarsAgo, pocPrice, endBarsAgo, pocPrice, brush, DashStyleHelper.Solid, 2);

			// Freshness: shade the part already retested since the zone formed. What
			// is left unshaded is the untouched volume still standing.
			double usedLow = 0, usedHigh = 0;
			bool haveUsed = false;
			if (z.IsSupply && z.PenHigh > double.MinValue)
			{
				usedLow  = z.Low;
				usedHigh = Math.Min(z.High, z.PenHigh);
				haveUsed = usedHigh > usedLow;
			}
			else if (!z.IsSupply && z.PenLow < double.MaxValue)
			{
				usedHigh = z.High;
				usedLow  = Math.Max(z.Low, z.PenLow);
				haveUsed = usedHigh > usedLow;
			}

			if (haveUsed)
				Draw.Rectangle(this, tag + "_used", false,
					startBarsAgo, usedHigh, endBarsAgo, usedLow, BrokenBrush, BrokenBrush, 10);
			else
				RemoveDrawObject(tag + "_used");
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name = "Departure length (bars)", Order = 0, GroupName = "Parameters")]
		public int LegBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Base width (bars)", Order = 1, GroupName = "Parameters")]
		public int BaseBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require volume on departure", Order = 2, GroupName = "Parameters")]
		public bool UseVolumeFilter { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Volume average lookback (bars)", Order = 3, GroupName = "Parameters")]
		public int VolumeLookback { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, double.MaxValue)]
		[Display(Name = "Volume multiple (x average)", Order = 4, GroupName = "Parameters")]
		public double VolumeMultiple { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Max zones per side", Order = 5, GroupName = "Parameters")]
		public int MaxZones { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Extend right (bars)", Order = 6, GroupName = "Parameters")]
		public int ExtendRightBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Base opacity (0-100)", Order = 7, GroupName = "Parameters")]
		public int BaseOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Order = 8, GroupName = "Parameters")]
		public bool ShowLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show broken zones", Order = 9, GroupName = "Parameters")]
		public bool ShowBroken { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "History (days of broken zones)", Order = 10, GroupName = "Parameters")]
		public int HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume at price (Volumetric bars)", Order = 11, GroupName = "Parameters")]
		public bool ShowVolume { get; set; }

		[XmlIgnore] [Display(Name = "Supply color", Order = 10, GroupName = "Visual")]
		public Brush SupplyBrush { get; set; }
		[Browsable(false)]
		public string SupplyBrushSerialize
		{
			get { return Serialize.BrushToString(SupplyBrush); }
			set { SupplyBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Demand color", Order = 11, GroupName = "Visual")]
		public Brush DemandBrush { get; set; }
		[Browsable(false)]
		public string DemandBrushSerialize
		{
			get { return Serialize.BrushToString(DemandBrush); }
			set { DemandBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Broken color", Order = 12, GroupName = "Visual")]
		public Brush BrokenBrush { get; set; }
		[Browsable(false)]
		public string BrokenBrushSerialize
		{
			get { return Serialize.BrushToString(BrokenBrush); }
			set { BrokenBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastVolumeZones[] cacheBallastVolumeZones;
		public BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			return BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken, historyDays, showVolume);
		}

		public BallastVolumeZones BallastVolumeZones(ISeries<double> input, int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			if (cacheBallastVolumeZones != null)
				for (int idx = 0; idx < cacheBallastVolumeZones.Length; idx++)
					if (cacheBallastVolumeZones[idx] != null && cacheBallastVolumeZones[idx].LegBars == legBars && cacheBallastVolumeZones[idx].BaseBars == baseBars && cacheBallastVolumeZones[idx].UseVolumeFilter == useVolumeFilter && cacheBallastVolumeZones[idx].VolumeLookback == volumeLookback && cacheBallastVolumeZones[idx].VolumeMultiple == volumeMultiple && cacheBallastVolumeZones[idx].MaxZones == maxZones && cacheBallastVolumeZones[idx].ExtendRightBars == extendRightBars && cacheBallastVolumeZones[idx].BaseOpacity == baseOpacity && cacheBallastVolumeZones[idx].ShowLabels == showLabels && cacheBallastVolumeZones[idx].ShowBroken == showBroken && cacheBallastVolumeZones[idx].HistoryDays == historyDays && cacheBallastVolumeZones[idx].ShowVolume == showVolume && cacheBallastVolumeZones[idx].EqualsInput(input))
						return cacheBallastVolumeZones[idx];
			return CacheIndicator<BallastVolumeZones>(new BallastVolumeZones(){ LegBars = legBars, BaseBars = baseBars, UseVolumeFilter = useVolumeFilter, VolumeLookback = volumeLookback, VolumeMultiple = volumeMultiple, MaxZones = maxZones, ExtendRightBars = extendRightBars, BaseOpacity = baseOpacity, ShowLabels = showLabels, ShowBroken = showBroken, HistoryDays = historyDays, ShowVolume = showVolume }, input, ref cacheBallastVolumeZones);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			return indicator.BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken, historyDays, showVolume);
		}

		public Indicators.BallastVolumeZones BallastVolumeZones(ISeries<double> input , int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			return indicator.BallastVolumeZones(input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken, historyDays, showVolume);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			return indicator.BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken, historyDays, showVolume);
		}

		public Indicators.BallastVolumeZones BallastVolumeZones(ISeries<double> input , int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken, int historyDays, bool showVolume)
		{
			return indicator.BallastVolumeZones(input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken, historyDays, showVolume);
		}
	}
}

#endregion
