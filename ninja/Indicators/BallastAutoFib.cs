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

// BallastAutoFib  (Ballast system palette: amber accents, neutral structure)
// ---------------------------------------------------------------------------
// Auto Fibonacci off the latest fractal swing leg. Fib ratios are public-domain
// math; swing detection is standard. Nothing copied from any third-party product.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastAutoFib : Indicator
	{
		private enum Kind { Anchor, Minor, Key, Extension }

		private double	p0Price, p1Price;
		private int		p0Bar,   p1Bar;
		private bool	p0IsHigh;
		private bool	haveP0, haveP1;

		private struct FibLevel
		{
			public double	Ratio;
			public string	Label;
			public Kind		Kind;
			public FibLevel(double r, string l, Kind k) { Ratio = r; Label = l; Kind = k; }
		}

		private FibLevel[] levels;
		private Brush minorBrush, anchorBrush, extBrush, legFaded;

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
				Description	= @"Auto Fibonacci retracement/extension off the latest swing leg (original).";
				Name		= "BallastAutoFib";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DisplayInDataBox		= false;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				Strength			= 5;
				ExtendRightBars		= 12;
				ShowPrices			= true;
				ShowGoldenPocket	= true;
				LineWidth			= 1;

				KeyBrush			= SB(0xFF, 0xC4, 0x00);		// amber
				RetracementBrush	= SB(0xFF, 0xB3, 0x00);		// dim amber
				ExtensionBrush		= SB(0x90, 0xA4, 0xAE);		// neutral gray
				LegBrush			= SB(0x90, 0xA4, 0xAE);		// neutral gray

				Show0236 = true;
				Show0382 = true;
				Show0500 = true;
				Show0618 = true;
				Show0786 = true;
				Show1000 = true;
				Show1272 = true;
				Show1618 = true;
			}
			else if (State == State.Configure)
			{
				levels = new FibLevel[]
				{
					new FibLevel(0.000, "0.0",   Kind.Anchor),
					new FibLevel(0.236, "23.6",  Kind.Minor),
					new FibLevel(0.382, "38.2",  Kind.Minor),
					new FibLevel(0.500, "50.0",  Kind.Key),
					new FibLevel(0.618, "61.8",  Kind.Key),
					new FibLevel(0.786, "78.6",  Kind.Minor),
					new FibLevel(1.000, "100.0", Kind.Anchor),
					new FibLevel(1.272, "127.2", Kind.Extension),
					new FibLevel(1.618, "161.8", Kind.Extension),
				};
			}
			else if (State == State.DataLoaded)
			{
				minorBrush	= Fade(RetracementBrush, 0.55);
				anchorBrush	= Fade(SB(0xB0, 0xBE, 0xC5), 0.75);	// light gray anchors
				extBrush	= Fade(ExtensionBrush, 0.80);
				legFaded	= Fade(LegBrush, 0.30);
			}
		}

		private Brush Fade(Brush b, double opacity)
		{
			SolidColorBrush scb = b as SolidColorBrush;
			Color c = scb != null ? scb.Color : Colors.Gray;
			SolidColorBrush nb = new SolidColorBrush(c) { Opacity = opacity };
			nb.Freeze();
			return nb;
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Strength * 2 + 1)
				return;

			int pivotIdx = Strength;
			double candHigh = High[pivotIdx];
			double candLow  = Low[pivotIdx];
			bool isSwingHigh = true;
			bool isSwingLow  = true;

			for (int i = 1; i <= Strength; i++)
			{
				if (High[pivotIdx + i] >= candHigh || High[pivotIdx - i] > candHigh) isSwingHigh = false;
				if (Low[pivotIdx + i]  <= candLow  || Low[pivotIdx - i]  < candLow)  isSwingLow  = false;
			}

			if (isSwingHigh)
				RegisterPivot(candHigh, CurrentBar - pivotIdx, true);
			else if (isSwingLow)
				RegisterPivot(candLow, CurrentBar - pivotIdx, false);

			if (haveP0 && haveP1)
				DrawFibs();
		}

		private void RegisterPivot(double price, int bar, bool isHigh)
		{
			if (!haveP0)
			{
				p0Price = price; p0Bar = bar; p0IsHigh = isHigh; haveP0 = true;
				return;
			}

			if (isHigh == p0IsHigh)
			{
				bool moreExtreme = isHigh ? price > p0Price : price < p0Price;
				if (moreExtreme) { p0Price = price; p0Bar = bar; }
			}
			else
			{
				p1Price = p0Price; p1Bar = p0Bar; haveP1 = true;
				p0Price = price;   p0Bar = bar;   p0IsHigh = isHigh;
			}
		}

		private bool Enabled(double ratio)
		{
			if (ratio == 0.000) return true;
			if (ratio == 0.236) return Show0236;
			if (ratio == 0.382) return Show0382;
			if (ratio == 0.500) return Show0500;
			if (ratio == 0.618) return Show0618;
			if (ratio == 0.786) return Show0786;
			if (ratio == 1.000) return Show1000;
			if (ratio == 1.272) return Show1272;
			if (ratio == 1.618) return Show1618;
			return false;
		}

		private double PriceOf(double ratio, double range)
		{
			return p0Price - ratio * range;
		}

		private void DrawFibs()
		{
			double range = p0Price - p1Price;
			int startBarsAgo = CurrentBar - p0Bar;
			int endBarsAgo   = -ExtendRightBars;

			Draw.Line(this, "BAF_leg", false,
				CurrentBar - p1Bar, p1Price,
				CurrentBar - p0Bar, p0Price,
				legFaded, DashStyleHelper.Dash, 1);

			if (ShowGoldenPocket && Show0618 && Show0786)
				Draw.Rectangle(this, "BAF_pocket", false,
					startBarsAgo, PriceOf(0.618, range),
					endBarsAgo,   PriceOf(0.786, range),
					Brushes.Transparent, KeyBrush, 12);
			else
				RemoveDrawObject("BAF_pocket");

			foreach (FibLevel lvl in levels)
			{
				string tag = "BAF_" + lvl.Label;

				if (!Enabled(lvl.Ratio))
				{
					RemoveDrawObject(tag);
					RemoveDrawObject(tag + "_t");
					continue;
				}

				double price = PriceOf(lvl.Ratio, range);

				Brush brush;
				DashStyleHelper dash;
				int width;

				switch (lvl.Kind)
				{
					case Kind.Key:
						brush = KeyBrush;      dash = DashStyleHelper.Solid; width = LineWidth + 2; break;
					case Kind.Anchor:
						brush = anchorBrush;   dash = DashStyleHelper.Solid; width = LineWidth + 1; break;
					case Kind.Extension:
						brush = extBrush;      dash = DashStyleHelper.Dot;   width = LineWidth;     break;
					default:
						brush = minorBrush;    dash = DashStyleHelper.Dot;   width = LineWidth;     break;
				}

				Draw.Line(this, tag, false,
					startBarsAgo, price,
					endBarsAgo,   price,
					brush, dash, width);

				if (ShowPrices)
				{
					string txt = lvl.Kind == Kind.Minor
						? lvl.Label + "%"
						: lvl.Label + "%  " + Instrument.MasterInstrument.FormatPrice(price);

					Brush textBrush = lvl.Kind == Kind.Key ? KeyBrush : brush;
					int fontSize = lvl.Kind == Kind.Key ? 11 : 10;

					Draw.Text(this, tag + "_t", false, txt,
						endBarsAgo, price, 0, textBrush,
						new NinjaTrader.Gui.Tools.SimpleFont("Arial", fontSize),
						System.Windows.TextAlignment.Left,
						Brushes.Transparent, Brushes.Black, 55);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Swing strength (bars each side)", Order = 0, GroupName = "Parameters")]
		public int Strength { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Extend right (bars)", Order = 1, GroupName = "Parameters")]
		public int ExtendRightBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Line width", Order = 2, GroupName = "Parameters")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show prices", Order = 3, GroupName = "Parameters")]
		public bool ShowPrices { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Shade golden pocket", Order = 4, GroupName = "Parameters")]
		public bool ShowGoldenPocket { get; set; }

		[NinjaScriptProperty] [Display(Name = "Show 23.6%",  Order = 10, GroupName = "Levels")] public bool Show0236 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 38.2%",  Order = 11, GroupName = "Levels")] public bool Show0382 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 50.0%",  Order = 12, GroupName = "Levels")] public bool Show0500 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 61.8%",  Order = 13, GroupName = "Levels")] public bool Show0618 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 78.6%",  Order = 14, GroupName = "Levels")] public bool Show0786 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 100.0%", Order = 15, GroupName = "Levels")] public bool Show1000 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 127.2%", Order = 16, GroupName = "Levels")] public bool Show1272 { get; set; }
		[NinjaScriptProperty] [Display(Name = "Show 161.8%", Order = 17, GroupName = "Levels")] public bool Show1618 { get; set; }

		[XmlIgnore] [Display(Name = "Key level color (50/61.8)", Order = 19, GroupName = "Visual")]
		public Brush KeyBrush { get; set; }
		[Browsable(false)]
		public string KeyBrushSerialize
		{
			get { return Serialize.BrushToString(KeyBrush); }
			set { KeyBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Retracement color", Order = 20, GroupName = "Visual")]
		public Brush RetracementBrush { get; set; }
		[Browsable(false)]
		public string RetracementBrushSerialize
		{
			get { return Serialize.BrushToString(RetracementBrush); }
			set { RetracementBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Extension color", Order = 21, GroupName = "Visual")]
		public Brush ExtensionBrush { get; set; }
		[Browsable(false)]
		public string ExtensionBrushSerialize
		{
			get { return Serialize.BrushToString(ExtensionBrush); }
			set { ExtensionBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Leg color", Order = 22, GroupName = "Visual")]
		public Brush LegBrush { get; set; }
		[Browsable(false)]
		public string LegBrushSerialize
		{
			get { return Serialize.BrushToString(LegBrush); }
			set { LegBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastAutoFib[] cacheBallastAutoFib;
		public BallastAutoFib BallastAutoFib(int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			return BallastAutoFib(Input, strength, extendRightBars, lineWidth, showPrices, showGoldenPocket, show0236, show0382, show0500, show0618, show0786, show1000, show1272, show1618);
		}

		public BallastAutoFib BallastAutoFib(ISeries<double> input, int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			if (cacheBallastAutoFib != null)
				for (int idx = 0; idx < cacheBallastAutoFib.Length; idx++)
					if (cacheBallastAutoFib[idx] != null && cacheBallastAutoFib[idx].Strength == strength && cacheBallastAutoFib[idx].ExtendRightBars == extendRightBars && cacheBallastAutoFib[idx].LineWidth == lineWidth && cacheBallastAutoFib[idx].ShowPrices == showPrices && cacheBallastAutoFib[idx].ShowGoldenPocket == showGoldenPocket && cacheBallastAutoFib[idx].Show0236 == show0236 && cacheBallastAutoFib[idx].Show0382 == show0382 && cacheBallastAutoFib[idx].Show0500 == show0500 && cacheBallastAutoFib[idx].Show0618 == show0618 && cacheBallastAutoFib[idx].Show0786 == show0786 && cacheBallastAutoFib[idx].Show1000 == show1000 && cacheBallastAutoFib[idx].Show1272 == show1272 && cacheBallastAutoFib[idx].Show1618 == show1618 && cacheBallastAutoFib[idx].EqualsInput(input))
						return cacheBallastAutoFib[idx];
			return CacheIndicator<BallastAutoFib>(new BallastAutoFib(){ Strength = strength, ExtendRightBars = extendRightBars, LineWidth = lineWidth, ShowPrices = showPrices, ShowGoldenPocket = showGoldenPocket, Show0236 = show0236, Show0382 = show0382, Show0500 = show0500, Show0618 = show0618, Show0786 = show0786, Show1000 = show1000, Show1272 = show1272, Show1618 = show1618 }, input, ref cacheBallastAutoFib);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastAutoFib BallastAutoFib(int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			return indicator.BallastAutoFib(Input, strength, extendRightBars, lineWidth, showPrices, showGoldenPocket, show0236, show0382, show0500, show0618, show0786, show1000, show1272, show1618);
		}

		public Indicators.BallastAutoFib BallastAutoFib(ISeries<double> input , int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			return indicator.BallastAutoFib(input, strength, extendRightBars, lineWidth, showPrices, showGoldenPocket, show0236, show0382, show0500, show0618, show0786, show1000, show1272, show1618);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastAutoFib BallastAutoFib(int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			return indicator.BallastAutoFib(Input, strength, extendRightBars, lineWidth, showPrices, showGoldenPocket, show0236, show0382, show0500, show0618, show0786, show1000, show1272, show1618);
		}

		public Indicators.BallastAutoFib BallastAutoFib(ISeries<double> input , int strength, int extendRightBars, int lineWidth, bool showPrices, bool showGoldenPocket, bool show0236, bool show0382, bool show0500, bool show0618, bool show0786, bool show1000, bool show1272, bool show1618)
		{
			return indicator.BallastAutoFib(input, strength, extendRightBars, lineWidth, showPrices, showGoldenPocket, show0236, show0382, show0500, show0618, show0786, show1000, show1272, show1618);
		}
	}
}

#endregion
