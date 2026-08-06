#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// BallastTrendScanner  (Ballast system palette card)
// ---------------------------------------------------------------------------
// Multi-timeframe trend dashboard. For each minute timeframe it compares a fast
// EMA to a slow EMA to classify trend (up/down/flat), then renders a compact
// card with each timeframe plus an overall "BULLS %". Standard EMA-cross read,
// written from scratch.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastTrendScanner : Indicator
	{
		private readonly int[]    tfMinutes = { 1, 3, 5, 10, 15, 30 };
		private readonly string[] tfNames   = { "1m", "3m", "5m", "10m", "15m", "30m" };
		private int[]  tfState;
		private int    bullPct;
		private int    bearPct;

		private static Brush SB(byte a, byte r, byte g, byte b)
		{
			SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
			br.Freeze();
			return br;
		}

		private Brush borderBrush;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"Multi-timeframe EMA trend dashboard card (original).";
				Name		= "BallastTrendScanner";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				FastPeriod	= 9;
				SlowPeriod	= 21;
				TextSize	= 13;
				PanelX		= 12;			// margin in from the RIGHT edge
				PanelY		= 16;			// margin up from the BOTTOM edge
				PanelWidth	= 148;

				UpBrush			= SB(255, 0x26, 0xA6, 0x9A);	// teal
				DownBrush		= SB(255, 0xEF, 0x53, 0x53);	// red
				NeutralBrush	= SB(255, 0x90, 0xA4, 0xAE);	// gray
				TextBrush		= SB(255, 0xCF, 0xD8, 0xDC);	// light gray
				BackgroundBrush	= SB(214, 0x12, 0x15, 0x1A);	// dark translucent card
			}
			else if (State == State.Configure)
			{
				tfState = new int[tfMinutes.Length];
				for (int i = 0; i < tfMinutes.Length; i++)
					AddDataSeries(BarsPeriodType.Minute, tfMinutes[i]);
			}
			else if (State == State.DataLoaded)
			{
				borderBrush = SB(60, 0xFF, 0xFF, 0xFF);			// faint white hairline
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			int up = 0, down = 0;
			for (int i = 0; i < tfMinutes.Length; i++)
			{
				int bip = i + 1;
				if (CurrentBars[bip] < SlowPeriod)
				{
					tfState[i] = 0;
					continue;
				}

				double fast = EMA(Closes[bip], FastPeriod)[0];
				double slow = EMA(Closes[bip], SlowPeriod)[0];
				tfState[i] = fast > slow ? 1 : fast < slow ? -1 : 0;
				if (tfState[i] > 0) up++;
				else if (tfState[i] < 0) down++;
			}

			bullPct = (int)Math.Round(100.0 * up   / tfMinutes.Length);
			bearPct = (int)Math.Round(100.0 * down / tfMinutes.Length);
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			if (RenderTarget == null || tfState == null || ChartPanel == null)
				return;

			float rowH = TextSize + 9;
			int   rows = 3 + tfMinutes.Length;			// header + BULLS + BEARS + timeframes
			float w = PanelWidth;
			float h = rowH * rows + 12;
			// Anchored to the chart's BOTTOM-RIGHT; PanelX / PanelY are now margins
			// in from the right and bottom edges.
			float x = ChartPanel.X + ChartPanel.W - w - PanelX;
			float y = ChartPanel.Y + ChartPanel.H - h - PanelY;

			// Card background + hairline border.
			var bg  = BackgroundBrush.ToDxBrush(RenderTarget);
			var bd  = borderBrush.ToDxBrush(RenderTarget);
			var rr  = new SharpDX.Direct2D1.RoundedRectangle
			{
				Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = 6f, RadiusY = 6f
			};
			RenderTarget.FillRoundedRectangle(rr, bg);
			RenderTarget.DrawRoundedRectangle(rr, bd, 1f);
			bg.Dispose();

			var fmt = new SharpDX.DirectWrite.TextFormat(
				NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", TextSize);

			float ty = y + 6;

			RenderText(x + 10, ty, "TREND SCANNER", TextBrush, fmt);
			ty += rowH;

			// Divider under the header.
			RenderTarget.DrawLine(new SharpDX.Vector2(x + 8, ty - 2),
				new SharpDX.Vector2(x + w - 8, ty - 2), bd, 1f);

			// Overall bias -- bulls and bears.
			Brush biasBrush = bullPct > 50 ? UpBrush : bullPct < 50 ? DownBrush : NeutralBrush;
			RenderText(x + 10, ty, "BULLS", TextBrush, fmt);
			RenderText(x + w * 0.55f, ty, bullPct + "%", biasBrush, fmt);
			ty += rowH;

			Brush bearBrush = bearPct > 50 ? DownBrush : bearPct < 50 ? UpBrush : NeutralBrush;
			RenderText(x + 10, ty, "BEARS", TextBrush, fmt);
			RenderText(x + w * 0.55f, ty, bearPct + "%", bearBrush, fmt);
			ty += rowH;

			for (int i = 0; i < tfMinutes.Length; i++)
			{
				string word = tfState[i] > 0 ? "UP" : tfState[i] < 0 ? "DOWN" : "--";
				Brush  wb   = tfState[i] > 0 ? UpBrush : tfState[i] < 0 ? DownBrush : NeutralBrush;

				RenderText(x + 10, ty, tfNames[i], TextBrush, fmt);
				RenderText(x + w * 0.55f, ty, word, wb, fmt);
				ty += rowH;
			}

			bd.Dispose();
			fmt.Dispose();
		}

		private void RenderText(float x, float y, string text, Brush brush, SharpDX.DirectWrite.TextFormat fmt)
		{
			var dx = brush.ToDxBrush(RenderTarget);
			var layout = new SharpDX.DirectWrite.TextLayout(
				NinjaTrader.Core.Globals.DirectWriteFactory, text, fmt, PanelWidth, TextSize + 6);
			RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), layout, dx);
			layout.Dispose();
			dx.Dispose();
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Fast EMA period", Order = 0, GroupName = "Parameters")]
		public int FastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Slow EMA period", Order = 1, GroupName = "Parameters")]
		public int SlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(6, 40)]
		[Display(Name = "Text size", Order = 2, GroupName = "Parameters")]
		public int TextSize { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Panel X offset", Order = 3, GroupName = "Parameters")]
		public int PanelX { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Panel Y offset", Order = 4, GroupName = "Parameters")]
		public int PanelY { get; set; }

		[NinjaScriptProperty]
		[Range(80, 400)]
		[Display(Name = "Panel width", Order = 5, GroupName = "Parameters")]
		public int PanelWidth { get; set; }

		[XmlIgnore] [Display(Name = "Up color", Order = 6, GroupName = "Visual")]
		public Brush UpBrush { get; set; }
		[Browsable(false)]
		public string UpBrushSerialize
		{
			get { return Serialize.BrushToString(UpBrush); }
			set { UpBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Down color", Order = 7, GroupName = "Visual")]
		public Brush DownBrush { get; set; }
		[Browsable(false)]
		public string DownBrushSerialize
		{
			get { return Serialize.BrushToString(DownBrush); }
			set { DownBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Neutral color", Order = 8, GroupName = "Visual")]
		public Brush NeutralBrush { get; set; }
		[Browsable(false)]
		public string NeutralBrushSerialize
		{
			get { return Serialize.BrushToString(NeutralBrush); }
			set { NeutralBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Label text color", Order = 9, GroupName = "Visual")]
		public Brush TextBrush { get; set; }
		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Panel background", Order = 10, GroupName = "Visual")]
		public Brush BackgroundBrush { get; set; }
		[Browsable(false)]
		public string BackgroundBrushSerialize
		{
			get { return Serialize.BrushToString(BackgroundBrush); }
			set { BackgroundBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastTrendScanner[] cacheBallastTrendScanner;
		public BallastTrendScanner BallastTrendScanner(int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			return BallastTrendScanner(Input, fastPeriod, slowPeriod, textSize, panelX, panelY, panelWidth);
		}

		public BallastTrendScanner BallastTrendScanner(ISeries<double> input, int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			if (cacheBallastTrendScanner != null)
				for (int idx = 0; idx < cacheBallastTrendScanner.Length; idx++)
					if (cacheBallastTrendScanner[idx] != null && cacheBallastTrendScanner[idx].FastPeriod == fastPeriod && cacheBallastTrendScanner[idx].SlowPeriod == slowPeriod && cacheBallastTrendScanner[idx].TextSize == textSize && cacheBallastTrendScanner[idx].PanelX == panelX && cacheBallastTrendScanner[idx].PanelY == panelY && cacheBallastTrendScanner[idx].PanelWidth == panelWidth && cacheBallastTrendScanner[idx].EqualsInput(input))
						return cacheBallastTrendScanner[idx];
			return CacheIndicator<BallastTrendScanner>(new BallastTrendScanner(){ FastPeriod = fastPeriod, SlowPeriod = slowPeriod, TextSize = textSize, PanelX = panelX, PanelY = panelY, PanelWidth = panelWidth }, input, ref cacheBallastTrendScanner);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastTrendScanner BallastTrendScanner(int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			return indicator.BallastTrendScanner(Input, fastPeriod, slowPeriod, textSize, panelX, panelY, panelWidth);
		}

		public Indicators.BallastTrendScanner BallastTrendScanner(ISeries<double> input , int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			return indicator.BallastTrendScanner(input, fastPeriod, slowPeriod, textSize, panelX, panelY, panelWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastTrendScanner BallastTrendScanner(int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			return indicator.BallastTrendScanner(Input, fastPeriod, slowPeriod, textSize, panelX, panelY, panelWidth);
		}

		public Indicators.BallastTrendScanner BallastTrendScanner(ISeries<double> input , int fastPeriod, int slowPeriod, int textSize, int panelX, int panelY, int panelWidth)
		{
			return indicator.BallastTrendScanner(input, fastPeriod, slowPeriod, textSize, panelX, panelY, panelWidth);
		}
	}
}

#endregion
