#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// BallastTrendBars  (Ballast system palette: teal up / red down, brighter = strong)
// ---------------------------------------------------------------------------
// Four-state trend-bar colorizer from EMA "force" + CCI + Williams %R. All
// three are public-domain indicators; the combination logic is original.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastTrendBars : Indicator
	{
		private EMA			force;
		private CCI			cci;
		private WilliamsR	wr;

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
				Description	= @"Four-state trend-bar colorizer (EMA force + CCI + Williams %R). Original.";
				Name		= "BallastTrendBars";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				ForcePeriod		= 50;
				CciPeriod		= 30;
				WilliamsPeriod	= 38;
				StrongCciLevel	= 100;

				UpBrush			= SB(0x1E, 0x7D, 0x74);		// muted teal (weak up)
				StrongUpBrush	= SB(0x1D, 0xE9, 0xB6);		// bright teal (strong up)
				DownBrush		= SB(0xB0, 0x3A, 0x38);		// muted red (weak down)
				StrongDownBrush	= SB(0xFF, 0x17, 0x44);		// bright red (strong down)
			}
			else if (State == State.Configure)
			{
				force	= EMA(ForcePeriod);
				cci		= CCI(CciPeriod);
				wr		= WilliamsR(WilliamsPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Math.Max(ForcePeriod, Math.Max(CciPeriod, WilliamsPeriod)))
				return;

			bool   trendUp = Close[0] > force[0];
			double cciVal  = cci[0];
			double wrVal   = wr[0];

			Brush c;
			if (trendUp)
				c = (cciVal >= StrongCciLevel && wrVal >= -50) ? StrongUpBrush : UpBrush;
			else
				c = (cciVal <= -StrongCciLevel && wrVal <= -50) ? StrongDownBrush : DownBrush;

			BarBrush			= c;
			CandleOutlineBrush	= c;
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Force EMA period", Order = 0, GroupName = "Parameters")]
		public int ForcePeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "CCI period", Order = 1, GroupName = "Parameters")]
		public int CciPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Williams %R period", Order = 2, GroupName = "Parameters")]
		public int WilliamsPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Strong CCI level", Order = 3, GroupName = "Parameters")]
		public int StrongCciLevel { get; set; }

		[XmlIgnore] [Display(Name = "Up (weak)", Order = 4, GroupName = "Visual")]
		public Brush UpBrush { get; set; }
		[Browsable(false)]
		public string UpBrushSerialize
		{
			get { return Serialize.BrushToString(UpBrush); }
			set { UpBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Up (strong)", Order = 5, GroupName = "Visual")]
		public Brush StrongUpBrush { get; set; }
		[Browsable(false)]
		public string StrongUpBrushSerialize
		{
			get { return Serialize.BrushToString(StrongUpBrush); }
			set { StrongUpBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Down (weak)", Order = 6, GroupName = "Visual")]
		public Brush DownBrush { get; set; }
		[Browsable(false)]
		public string DownBrushSerialize
		{
			get { return Serialize.BrushToString(DownBrush); }
			set { DownBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Down (strong)", Order = 7, GroupName = "Visual")]
		public Brush StrongDownBrush { get; set; }
		[Browsable(false)]
		public string StrongDownBrushSerialize
		{
			get { return Serialize.BrushToString(StrongDownBrush); }
			set { StrongDownBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastTrendBars[] cacheBallastTrendBars;
		public BallastTrendBars BallastTrendBars(int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			return BallastTrendBars(Input, forcePeriod, cciPeriod, williamsPeriod, strongCciLevel);
		}

		public BallastTrendBars BallastTrendBars(ISeries<double> input, int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			if (cacheBallastTrendBars != null)
				for (int idx = 0; idx < cacheBallastTrendBars.Length; idx++)
					if (cacheBallastTrendBars[idx] != null && cacheBallastTrendBars[idx].ForcePeriod == forcePeriod && cacheBallastTrendBars[idx].CciPeriod == cciPeriod && cacheBallastTrendBars[idx].WilliamsPeriod == williamsPeriod && cacheBallastTrendBars[idx].StrongCciLevel == strongCciLevel && cacheBallastTrendBars[idx].EqualsInput(input))
						return cacheBallastTrendBars[idx];
			return CacheIndicator<BallastTrendBars>(new BallastTrendBars(){ ForcePeriod = forcePeriod, CciPeriod = cciPeriod, WilliamsPeriod = williamsPeriod, StrongCciLevel = strongCciLevel }, input, ref cacheBallastTrendBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastTrendBars BallastTrendBars(int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			return indicator.BallastTrendBars(Input, forcePeriod, cciPeriod, williamsPeriod, strongCciLevel);
		}

		public Indicators.BallastTrendBars BallastTrendBars(ISeries<double> input , int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			return indicator.BallastTrendBars(input, forcePeriod, cciPeriod, williamsPeriod, strongCciLevel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastTrendBars BallastTrendBars(int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			return indicator.BallastTrendBars(Input, forcePeriod, cciPeriod, williamsPeriod, strongCciLevel);
		}

		public Indicators.BallastTrendBars BallastTrendBars(ISeries<double> input , int forcePeriod, int cciPeriod, int williamsPeriod, int strongCciLevel)
		{
			return indicator.BallastTrendBars(input, forcePeriod, cciPeriod, williamsPeriod, strongCciLevel);
		}
	}
}

#endregion
