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

// BallastTrendExhaustion  (Ballast system palette: neutral skeleton, teal/red structure tags)
// ---------------------------------------------------------------------------
// ZigZag swing / trend-exhaustion line. The leg line is drawn neutral so it
// reads as structure over the colored candles; the HH/HL/LH/LL tags carry the
// trend color. Public-domain ZigZag technique, written from scratch.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastTrendExhaustion : Indicator
	{
		private int		dir;
		private double	extPrice;
		private int		extBar;
		private double	lastPivotPrice;
		private int		lastPivotBar;
		private int		segId;
		private double	lastHighPrice = double.MinValue;
		private double	lastLowPrice  = double.MaxValue;

		private static Brush SB(byte r, byte g, byte b)
		{
			SolidColorBrush br = new SolidColorBrush(Color.FromRgb(r, g, b));
			br.Freeze();
			return br;
		}

		// Structure-tag colors (bullish structure = teal, bearish = red).
		private static readonly Brush BullBrush = SB(0x26, 0xA6, 0x9A);
		private static readonly Brush BearBrush = SB(0xEF, 0x53, 0x53);

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"ZigZag-based swing / trend-exhaustion line (original).";
				Name		= "BallastTrendExhaustion";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				UsePoints			= true;
				DeviationPoints		= 20;
				DeviationPercent	= 0.5;
				MinBars				= 2;
				ShowDots			= true;
				ShowSwingLabels		= true;
				LineWidth			= 2;

				// Neutral skeleton so the zigzag reads over the colored candles.
				UpBrush		= SB(0xB0, 0xBE, 0xC5);
				DownBrush	= SB(0x78, 0x90, 0x9C);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
			{
				extPrice		= Close[0];
				extBar			= CurrentBar;
				lastPivotPrice	= Close[0];
				lastPivotBar	= CurrentBar;
				dir				= 0;
				return;
			}

			double threshold = UsePoints ? DeviationPoints : extPrice * DeviationPercent / 100.0;

			if (dir >= 0)
			{
				if (High[0] > extPrice)
				{
					extPrice = High[0];
					extBar   = CurrentBar;
				}
				else if (extPrice - Low[0] >= threshold && (CurrentBar - extBar) >= MinBars)
				{
					ConfirmPivot(true);
					dir      = -1;
					extPrice = Low[0];
					extBar   = CurrentBar;
				}
			}
			else
			{
				if (Low[0] < extPrice)
				{
					extPrice = Low[0];
					extBar   = CurrentBar;
				}
				else if (High[0] - extPrice >= threshold && (CurrentBar - extBar) >= MinBars)
				{
					ConfirmPivot(false);
					dir      = 1;
					extPrice = High[0];
					extBar   = CurrentBar;
				}
			}

			Brush liveBrush = dir >= 0 ? UpBrush : DownBrush;
			Draw.Line(this, "TE_live", false,
				CurrentBar - lastPivotBar, lastPivotPrice,
				CurrentBar - extBar,       extPrice,
				liveBrush, DashStyleHelper.Solid, LineWidth);
		}

		private void ConfirmPivot(bool legWasUp)
		{
			Brush brush = legWasUp ? UpBrush : DownBrush;
			string tag  = "TE_seg" + segId;

			Draw.Line(this, tag, false,
				CurrentBar - lastPivotBar, lastPivotPrice,
				CurrentBar - extBar,       extPrice,
				brush, DashStyleHelper.Solid, LineWidth);

			if (ShowDots)
				Draw.Dot(this, "TE_dot" + segId, false,
					CurrentBar - extBar, extPrice, brush);

			if (ShowSwingLabels)
			{
				string label;
				int yOffset;
				if (legWasUp)
				{
					label   = extPrice > lastHighPrice ? "HH" : "LH";
					yOffset = -14;
					lastHighPrice = extPrice;
				}
				else
				{
					label   = extPrice < lastLowPrice ? "LL" : "HL";
					yOffset = 14;
					lastLowPrice = extPrice;
				}

				// HH / HL = bullish structure (teal); LH / LL = bearish (red).
				bool bull = label == "HH" || label == "HL";
				Brush lblBrush = bull ? BullBrush : BearBrush;

				Draw.Text(this, "TE_lbl" + segId, false, label,
					CurrentBar - extBar, extPrice, yOffset, lblBrush,
					new NinjaTrader.Gui.Tools.SimpleFont("Arial", 12),
					System.Windows.TextAlignment.Center,
					Brushes.Transparent, Brushes.Black, 60);
			}

			lastPivotPrice = extPrice;
			lastPivotBar   = extBar;
			segId++;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Use points (else percent)", Order = 0, GroupName = "Parameters")]
		public bool UsePoints { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Reversal threshold (points)", Order = 1, GroupName = "Parameters")]
		public double DeviationPoints { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Reversal threshold (percent)", Order = 2, GroupName = "Parameters")]
		public double DeviationPercent { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Min bars per leg", Order = 3, GroupName = "Parameters")]
		public int MinBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show pivot dots", Order = 4, GroupName = "Parameters")]
		public bool ShowDots { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show swing labels (HH/HL/LH/LL)", Order = 5, GroupName = "Parameters")]
		public bool ShowSwingLabels { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Line width", Order = 6, GroupName = "Parameters")]
		public int LineWidth { get; set; }

		[XmlIgnore] [Display(Name = "Up leg color", Order = 7, GroupName = "Visual")]
		public Brush UpBrush { get; set; }
		[Browsable(false)]
		public string UpBrushSerialize
		{
			get { return Serialize.BrushToString(UpBrush); }
			set { UpBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Down leg color", Order = 8, GroupName = "Visual")]
		public Brush DownBrush { get; set; }
		[Browsable(false)]
		public string DownBrushSerialize
		{
			get { return Serialize.BrushToString(DownBrush); }
			set { DownBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastTrendExhaustion[] cacheBallastTrendExhaustion;
		public BallastTrendExhaustion BallastTrendExhaustion(bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			return BallastTrendExhaustion(Input, usePoints, deviationPoints, deviationPercent, minBars, showDots, showSwingLabels, lineWidth);
		}

		public BallastTrendExhaustion BallastTrendExhaustion(ISeries<double> input, bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			if (cacheBallastTrendExhaustion != null)
				for (int idx = 0; idx < cacheBallastTrendExhaustion.Length; idx++)
					if (cacheBallastTrendExhaustion[idx] != null && cacheBallastTrendExhaustion[idx].UsePoints == usePoints && cacheBallastTrendExhaustion[idx].DeviationPoints == deviationPoints && cacheBallastTrendExhaustion[idx].DeviationPercent == deviationPercent && cacheBallastTrendExhaustion[idx].MinBars == minBars && cacheBallastTrendExhaustion[idx].ShowDots == showDots && cacheBallastTrendExhaustion[idx].ShowSwingLabels == showSwingLabels && cacheBallastTrendExhaustion[idx].LineWidth == lineWidth && cacheBallastTrendExhaustion[idx].EqualsInput(input))
						return cacheBallastTrendExhaustion[idx];
			return CacheIndicator<BallastTrendExhaustion>(new BallastTrendExhaustion(){ UsePoints = usePoints, DeviationPoints = deviationPoints, DeviationPercent = deviationPercent, MinBars = minBars, ShowDots = showDots, ShowSwingLabels = showSwingLabels, LineWidth = lineWidth }, input, ref cacheBallastTrendExhaustion);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastTrendExhaustion BallastTrendExhaustion(bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			return indicator.BallastTrendExhaustion(Input, usePoints, deviationPoints, deviationPercent, minBars, showDots, showSwingLabels, lineWidth);
		}

		public Indicators.BallastTrendExhaustion BallastTrendExhaustion(ISeries<double> input , bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			return indicator.BallastTrendExhaustion(input, usePoints, deviationPoints, deviationPercent, minBars, showDots, showSwingLabels, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastTrendExhaustion BallastTrendExhaustion(bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			return indicator.BallastTrendExhaustion(Input, usePoints, deviationPoints, deviationPercent, minBars, showDots, showSwingLabels, lineWidth);
		}

		public Indicators.BallastTrendExhaustion BallastTrendExhaustion(ISeries<double> input , bool usePoints, double deviationPoints, double deviationPercent, int minBars, bool showDots, bool showSwingLabels, int lineWidth)
		{
			return indicator.BallastTrendExhaustion(input, usePoints, deviationPoints, deviationPercent, minBars, showDots, showSwingLabels, lineWidth);
		}
	}
}

#endregion
