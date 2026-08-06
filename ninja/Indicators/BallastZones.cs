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

// BallastZones  (Ballast system palette: faint teal support / faint red resistance)
// ---------------------------------------------------------------------------
// Support/resistance zones with a one-time role reversal lifecycle:
//   * a swing high is born as resistance, a swing low as support
//   * when price CLOSES through it, the level flips role ONCE
//     (broken resistance -> support, broken support -> resistance)
//   * when price closes through the flipped level, it is retired for good
// So a level flips at most once and is then gone -- no perpetual flickering as
// price chops across it. Standard S/R technique, written from scratch.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastZones : Indicator
	{
		private class Level
		{
			public double Price;
			public int    Bar;
			public bool   IsRes;		// current role
			public bool   Flipped;		// has it already reversed once?
			public bool   Alive = true;
		}

		private List<Level> levels = new List<Level>();
		private Brush resLine, supLine;

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
				Description	= @"Support/resistance zones with one-time role reversal (original).";
				Name		= "BallastZones";
				Calculate	= Calculate.OnBarClose;
				IsOverlay	= true;
				DrawOnPricePanel		= true;
				IsSuspendedWhileInactive	= true;

				Strength		= 5;
				ZoneTicks		= 8;
				MaxZones		= 3;
				ExtendRightBars	= 10;
				ShowLabels		= true;
				ZoneOpacity		= 12;

				ResistanceBrush	= SB(0xEF, 0x53, 0x53);		// red
				SupportBrush	= SB(0x26, 0xA6, 0x9A);		// teal
			}
			else if (State == State.DataLoaded)
			{
				resLine = ResistanceBrush;
				supLine = SupportBrush;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Strength * 2 + 1)
				return;

			// 1) New confirmed pivot -> new level (high = resistance, low = support).
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
				levels.Add(new Level { Price = candHigh, Bar = CurrentBar - pivotIdx, IsRes = true });
			else if (isSwingLow)
				levels.Add(new Level { Price = candLow,  Bar = CurrentBar - pivotIdx, IsRes = false });

			// 2) Advance each level's lifecycle on a close-through.
			double close = Close[0];
			foreach (Level lv in levels)
			{
				if (!lv.Alive)
					continue;

				bool broken = lv.IsRes ? close > lv.Price : close < lv.Price;
				if (!broken)
					continue;

				if (!lv.Flipped)
				{
					lv.IsRes   = !lv.IsRes;		// first break -> flip role once
					lv.Flipped = true;
				}
				else
				{
					lv.Alive = false;			// second break -> retire for good
				}
			}

			DrawZones();
		}

		private void DrawZones()
		{
			double buf    = ZoneTicks * TickSize;
			double minSep = Math.Max(buf * 2.0, TickSize);
			double close  = Close[0];

			List<Level> res = new List<Level>();
			List<Level> sup = new List<Level>();
			foreach (Level lv in levels)
			{
				if (!lv.Alive) continue;
				if (lv.IsRes) res.Add(lv); else sup.Add(lv);
			}

			res.Sort((a, b) => Math.Abs(a.Price - close).CompareTo(Math.Abs(b.Price - close)));
			sup.Sort((a, b) => Math.Abs(a.Price - close).CompareTo(Math.Abs(b.Price - close)));

			DrawSide(res, minSep, buf, true);
			DrawSide(sup, minSep, buf, false);
		}

		private void DrawSide(List<Level> candidates, double minSep, double buf, bool isResistance)
		{
			string prefix = isResistance ? "Z_res" : "Z_sup";
			string sym    = isResistance ? "R" : "S";
			Brush  fill   = isResistance ? ResistanceBrush : SupportBrush;
			Brush  line   = isResistance ? resLine : supLine;
			int    endBarsAgo = -ExtendRightBars;

			List<Level> chosen = new List<Level>();
			foreach (Level lv in candidates)
			{
				bool tooClose = false;
				foreach (Level j in chosen)
					if (Math.Abs(lv.Price - j.Price) < minSep) { tooClose = true; break; }
				if (!tooClose)
					chosen.Add(lv);
				if (chosen.Count >= MaxZones)
					break;
			}

			for (int i = 0; i < MaxZones; i++)
			{
				string tag = prefix + i;

				if (i >= chosen.Count)
				{
					RemoveDrawObject(tag);
					RemoveDrawObject(tag + "_c");
					RemoveDrawObject(tag + "_t");
					continue;
				}

				Level  lv = chosen[i];
				double price = lv.Price;
				int    startBarsAgo = CurrentBar - lv.Bar;

				Draw.Rectangle(this, tag, false,
					startBarsAgo, price + buf, endBarsAgo, price - buf,
					Brushes.Transparent, fill, ZoneOpacity);

				Draw.Line(this, tag + "_c", false,
					startBarsAgo, price, endBarsAgo, price,
					line, DashStyleHelper.Dot, 1);

				if (ShowLabels)
				{
					double labelY = isResistance ? price + buf : price - buf;
					Draw.Text(this, tag + "_t", false,
						sym + "  " + Instrument.MasterInstrument.FormatPrice(price),
						startBarsAgo, labelY, 0, line,
						new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9),
						System.Windows.TextAlignment.Right,
						Brushes.Transparent, Brushes.Black, 45);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Swing strength (bars each side)", Order = 0, GroupName = "Parameters")]
		public int Strength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Zone half-thickness (ticks)", Order = 1, GroupName = "Parameters")]
		public int ZoneTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Max zones per side", Order = 2, GroupName = "Parameters")]
		public int MaxZones { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Extend right (bars)", Order = 3, GroupName = "Parameters")]
		public int ExtendRightBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Zone opacity (0-100)", Order = 4, GroupName = "Parameters")]
		public int ZoneOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Order = 5, GroupName = "Parameters")]
		public bool ShowLabels { get; set; }

		[XmlIgnore] [Display(Name = "Resistance color", Order = 6, GroupName = "Visual")]
		public Brush ResistanceBrush { get; set; }
		[Browsable(false)]
		public string ResistanceBrushSerialize
		{
			get { return Serialize.BrushToString(ResistanceBrush); }
			set { ResistanceBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] [Display(Name = "Support color", Order = 7, GroupName = "Visual")]
		public Brush SupportBrush { get; set; }
		[Browsable(false)]
		public string SupportBrushSerialize
		{
			get { return Serialize.BrushToString(SupportBrush); }
			set { SupportBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastZones[] cacheBallastZones;
		public BallastZones BallastZones(int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			return BallastZones(Input, strength, zoneTicks, maxZones, extendRightBars, zoneOpacity, showLabels);
		}

		public BallastZones BallastZones(ISeries<double> input, int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			if (cacheBallastZones != null)
				for (int idx = 0; idx < cacheBallastZones.Length; idx++)
					if (cacheBallastZones[idx] != null && cacheBallastZones[idx].Strength == strength && cacheBallastZones[idx].ZoneTicks == zoneTicks && cacheBallastZones[idx].MaxZones == maxZones && cacheBallastZones[idx].ExtendRightBars == extendRightBars && cacheBallastZones[idx].ZoneOpacity == zoneOpacity && cacheBallastZones[idx].ShowLabels == showLabels && cacheBallastZones[idx].EqualsInput(input))
						return cacheBallastZones[idx];
			return CacheIndicator<BallastZones>(new BallastZones(){ Strength = strength, ZoneTicks = zoneTicks, MaxZones = maxZones, ExtendRightBars = extendRightBars, ZoneOpacity = zoneOpacity, ShowLabels = showLabels }, input, ref cacheBallastZones);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastZones BallastZones(int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			return indicator.BallastZones(Input, strength, zoneTicks, maxZones, extendRightBars, zoneOpacity, showLabels);
		}

		public Indicators.BallastZones BallastZones(ISeries<double> input , int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			return indicator.BallastZones(input, strength, zoneTicks, maxZones, extendRightBars, zoneOpacity, showLabels);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastZones BallastZones(int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			return indicator.BallastZones(Input, strength, zoneTicks, maxZones, extendRightBars, zoneOpacity, showLabels);
		}

		public Indicators.BallastZones BallastZones(ISeries<double> input , int strength, int zoneTicks, int maxZones, int extendRightBars, int zoneOpacity, bool showLabels)
		{
			return indicator.BallastZones(input, strength, zoneTicks, maxZones, extendRightBars, zoneOpacity, showLabels);
		}
	}
}

#endregion
