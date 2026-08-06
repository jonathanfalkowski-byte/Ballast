#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// BallastDailyTargets
// ---------------------------------------------------------------------------
// Original clean-room "daily targets" indicator. It measures the high and low
// made during a defined session window (Start .. End, chart time zone) and
// holds those two levels as horizontal target lines for the rest of the day.
// Window is specified to the minute. Standard session range logic, from scratch.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastDailyTargets : Indicator
	{
		private DateTime	currentDay = DateTime.MinValue;
		private double		sessHigh;
		private double		sessLow;
		private bool		haveRange;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"Session-window high/low held as daily target lines (original).";
				Name		= "BallastDailyTargets";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				PaintPriceMarkers		= true;
				IsSuspendedWhileInactive	= true;

				// Default RTH-morning window 09:30 - 12:30 (chart time zone).
				StartHour	= 9;
				StartMinute	= 30;
				EndHour		= 12;
				EndMinute	= 30;

				ShowLabels	= true;
				HighLabel	= "Target Hi";
				LowLabel	= "Target Lo";

				AddPlot(new Stroke(Brushes.Orange,        2), PlotStyle.Hash, "DayHigh");
				AddPlot(new Stroke(Brushes.DeepSkyBlue,   2), PlotStyle.Hash, "DayLow");
			}
		}

		protected override void OnBarUpdate()
		{
			DateTime day = Time[0].Date;

			// New calendar day -> reset the measured range.
			if (day != currentDay)
			{
				currentDay = day;
				sessHigh   = double.MinValue;
				sessLow    = double.MaxValue;
				haveRange  = false;
			}

			// Minute-of-day comparison so half-hour boundaries (09:30) work.
			int tod      = Time[0].Hour * 60 + Time[0].Minute;
			int startTod = StartHour   * 60 + StartMinute;
			int endTod   = EndHour     * 60 + EndMinute;

			bool inWindow = tod >= startTod && tod < endTod;
			if (inWindow)
			{
				if (High[0] > sessHigh) sessHigh = High[0];
				if (Low[0]  < sessLow)  sessLow  = Low[0];
				haveRange = true;
			}

			if (haveRange)
			{
				DayHigh[0] = sessHigh;
				DayLow[0]  = sessLow;

				if (ShowLabels)
				{
					Draw.Text(this, "BDT_hiLbl", false,
						HighLabel + "  " + Instrument.MasterInstrument.FormatPrice(sessHigh),
						0, sessHigh, 8, Plots[0].Brush,
						new NinjaTrader.Gui.Tools.SimpleFont("Arial", 11),
						System.Windows.TextAlignment.Left,
						Brushes.Transparent, Brushes.Black, 55);

					Draw.Text(this, "BDT_loLbl", false,
						LowLabel + "  " + Instrument.MasterInstrument.FormatPrice(sessLow),
						0, sessLow, -8, Plots[1].Brush,
						new NinjaTrader.Gui.Tools.SimpleFont("Arial", 11),
						System.Windows.TextAlignment.Left,
						Brushes.Transparent, Brushes.Black, 55);
				}
			}
			else
			{
				DayHigh[0] = double.NaN;
				DayLow[0]  = double.NaN;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Start hour", Order = 0, GroupName = "Session window")]
		public int StartHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Start minute", Order = 1, GroupName = "Session window")]
		public int StartMinute { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "End hour", Order = 2, GroupName = "Session window")]
		public int EndHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "End minute", Order = 3, GroupName = "Session window")]
		public int EndMinute { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Order = 4, GroupName = "Parameters")]
		public bool ShowLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "High label text", Order = 5, GroupName = "Parameters")]
		public string HighLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Low label text", Order = 6, GroupName = "Parameters")]
		public string LowLabel { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DayHigh { get { return Values[0]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DayLow { get { return Values[1]; } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastDailyTargets[] cacheBallastDailyTargets;
		public BallastDailyTargets BallastDailyTargets(int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			return BallastDailyTargets(Input, startHour, startMinute, endHour, endMinute, showLabels, highLabel, lowLabel);
		}

		public BallastDailyTargets BallastDailyTargets(ISeries<double> input, int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			if (cacheBallastDailyTargets != null)
				for (int idx = 0; idx < cacheBallastDailyTargets.Length; idx++)
					if (cacheBallastDailyTargets[idx] != null && cacheBallastDailyTargets[idx].StartHour == startHour && cacheBallastDailyTargets[idx].StartMinute == startMinute && cacheBallastDailyTargets[idx].EndHour == endHour && cacheBallastDailyTargets[idx].EndMinute == endMinute && cacheBallastDailyTargets[idx].ShowLabels == showLabels && cacheBallastDailyTargets[idx].HighLabel == highLabel && cacheBallastDailyTargets[idx].LowLabel == lowLabel && cacheBallastDailyTargets[idx].EqualsInput(input))
						return cacheBallastDailyTargets[idx];
			return CacheIndicator<BallastDailyTargets>(new BallastDailyTargets(){ StartHour = startHour, StartMinute = startMinute, EndHour = endHour, EndMinute = endMinute, ShowLabels = showLabels, HighLabel = highLabel, LowLabel = lowLabel }, input, ref cacheBallastDailyTargets);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastDailyTargets BallastDailyTargets(int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			return indicator.BallastDailyTargets(Input, startHour, startMinute, endHour, endMinute, showLabels, highLabel, lowLabel);
		}

		public Indicators.BallastDailyTargets BallastDailyTargets(ISeries<double> input , int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			return indicator.BallastDailyTargets(input, startHour, startMinute, endHour, endMinute, showLabels, highLabel, lowLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastDailyTargets BallastDailyTargets(int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			return indicator.BallastDailyTargets(Input, startHour, startMinute, endHour, endMinute, showLabels, highLabel, lowLabel);
		}

		public Indicators.BallastDailyTargets BallastDailyTargets(ISeries<double> input , int startHour, int startMinute, int endHour, int endMinute, bool showLabels, string highLabel, string lowLabel)
		{
			return indicator.BallastDailyTargets(input, startHour, startMinute, endHour, endMinute, showLabels, highLabel, lowLabel);
		}
	}
}

#endregion
