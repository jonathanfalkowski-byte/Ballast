#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;   // makes BallastMaMethod visible to NT's generated overloads
#endregion

// BallastMaTrend  (Ballast system palette: teal up / red down)
// ---------------------------------------------------------------------------
// Slope-colored moving average trend line. Standard public-domain MA math.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public enum BallastMaMethod { SMA, EMA, WMA, HMA, TEMA }

	public class BallastMaTrend : Indicator
	{
		private Indicator ma;

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
				Description	= @"Slope-colored moving average trend line (original).";
				Name		= "BallastMaTrend";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DisplayInDataBox	= true;
				DrawOnPricePanel	= true;
				PaintPriceMarkers	= true;
				IsSuspendedWhileInactive	= true;

				Method		= BallastMaMethod.EMA;
				Period		= 30;
				LineWidth	= 3;
				UpBrush		= SB(0x26, 0xA6, 0x9A);		// teal
				DownBrush	= SB(0xEF, 0x53, 0x53);		// red

				AddPlot(new Stroke(SB(0x26, 0xA6, 0x9A), 3), PlotStyle.Line, "Trend");
			}
			else if (State == State.Configure)
			{
				switch (Method)
				{
					case BallastMaMethod.SMA:	ma = SMA(Period);	break;
					case BallastMaMethod.EMA:	ma = EMA(Period);	break;
					case BallastMaMethod.WMA:	ma = WMA(Period);	break;
					case BallastMaMethod.HMA:	ma = HMA(Period);	break;
					case BallastMaMethod.TEMA:	ma = TEMA(Period);	break;
					default:					ma = EMA(Period);	break;
				}
			}
			else if (State == State.DataLoaded)
			{
				Plots[0].Width = LineWidth;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Period)
				return;

			Value[0] = ma[0];
			if (CurrentBar > 0)
				PlotBrushes[0][0] = Value[0] >= Value[1] ? UpBrush : DownBrush;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "MA method", Order = 0, GroupName = "Parameters")]
		public BallastMaMethod Method { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Period", Order = 1, GroupName = "Parameters")]
		public int Period { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Line width", Order = 2, GroupName = "Parameters")]
		public int LineWidth { get; set; }

		[XmlIgnore] [Display(Name = "Up (rising) color", Order = 3, GroupName = "Visual")]
		public Brush UpBrush { get; set; }
		[Browsable(false)]
		public string UpBrushSerialize
		{
			get { return Serialize.BrushToString(UpBrush); }
			set { UpBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Down (falling) color", Order = 4, GroupName = "Visual")]
		public Brush DownBrush { get; set; }
		[Browsable(false)]
		public string DownBrushSerialize
		{
			get { return Serialize.BrushToString(DownBrush); }
			set { DownBrush = Serialize.StringToBrush(value); }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Trend { get { return Values[0]; } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastMaTrend[] cacheBallastMaTrend;
		public BallastMaTrend BallastMaTrend(BallastMaMethod method, int period, int lineWidth)
		{
			return BallastMaTrend(Input, method, period, lineWidth);
		}

		public BallastMaTrend BallastMaTrend(ISeries<double> input, BallastMaMethod method, int period, int lineWidth)
		{
			if (cacheBallastMaTrend != null)
				for (int idx = 0; idx < cacheBallastMaTrend.Length; idx++)
					if (cacheBallastMaTrend[idx] != null && cacheBallastMaTrend[idx].Method == method && cacheBallastMaTrend[idx].Period == period && cacheBallastMaTrend[idx].LineWidth == lineWidth && cacheBallastMaTrend[idx].EqualsInput(input))
						return cacheBallastMaTrend[idx];
			return CacheIndicator<BallastMaTrend>(new BallastMaTrend(){ Method = method, Period = period, LineWidth = lineWidth }, input, ref cacheBallastMaTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastMaTrend BallastMaTrend(BallastMaMethod method, int period, int lineWidth)
		{
			return indicator.BallastMaTrend(Input, method, period, lineWidth);
		}

		public Indicators.BallastMaTrend BallastMaTrend(ISeries<double> input , BallastMaMethod method, int period, int lineWidth)
		{
			return indicator.BallastMaTrend(input, method, period, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastMaTrend BallastMaTrend(BallastMaMethod method, int period, int lineWidth)
		{
			return indicator.BallastMaTrend(Input, method, period, lineWidth);
		}

		public Indicators.BallastMaTrend BallastMaTrend(ISeries<double> input , BallastMaMethod method, int period, int lineWidth)
		{
			return indicator.BallastMaTrend(input, method, period, lineWidth);
		}
	}
}

#endregion
