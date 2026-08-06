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
			public double Ratio;

			public double Center { get { return (Low + High) * 0.5; } }
			public double Height { get { return High - Low; } }
		}

		private List<Zone> zones = new List<Zone>();

		private int curDir;
		private int runLen;
		private int runStartBar;

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

				SupplyBrush	= SB(0xEF, 0x53, 0x53);
				DemandBrush	= SB(0x26, 0xA6, 0x9A);
				BrokenBrush	= SB(0x60, 0x6B, 0x78);
			}
		}

		protected override void OnBarUpdate()
		{
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
					z.Broken    = true;
					z.BrokenBar = CurrentBar;
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
			DrawList(ShowBroken ? SelectNearest(broken, close) : new List<Zone>(),
				"VZ_brk", BrokenBrush, false, brokenOp, "x", false, true);
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
		public BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			return BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken);
		}

		public BallastVolumeZones BallastVolumeZones(ISeries<double> input, int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			if (cacheBallastVolumeZones != null)
				for (int idx = 0; idx < cacheBallastVolumeZones.Length; idx++)
					if (cacheBallastVolumeZones[idx] != null && cacheBallastVolumeZones[idx].LegBars == legBars && cacheBallastVolumeZones[idx].BaseBars == baseBars && cacheBallastVolumeZones[idx].UseVolumeFilter == useVolumeFilter && cacheBallastVolumeZones[idx].VolumeLookback == volumeLookback && cacheBallastVolumeZones[idx].VolumeMultiple == volumeMultiple && cacheBallastVolumeZones[idx].MaxZones == maxZones && cacheBallastVolumeZones[idx].ExtendRightBars == extendRightBars && cacheBallastVolumeZones[idx].BaseOpacity == baseOpacity && cacheBallastVolumeZones[idx].ShowLabels == showLabels && cacheBallastVolumeZones[idx].ShowBroken == showBroken && cacheBallastVolumeZones[idx].EqualsInput(input))
						return cacheBallastVolumeZones[idx];
			return CacheIndicator<BallastVolumeZones>(new BallastVolumeZones(){ LegBars = legBars, BaseBars = baseBars, UseVolumeFilter = useVolumeFilter, VolumeLookback = volumeLookback, VolumeMultiple = volumeMultiple, MaxZones = maxZones, ExtendRightBars = extendRightBars, BaseOpacity = baseOpacity, ShowLabels = showLabels, ShowBroken = showBroken }, input, ref cacheBallastVolumeZones);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			return indicator.BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken);
		}

		public Indicators.BallastVolumeZones BallastVolumeZones(ISeries<double> input , int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			return indicator.BallastVolumeZones(input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastVolumeZones BallastVolumeZones(int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			return indicator.BallastVolumeZones(Input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken);
		}

		public Indicators.BallastVolumeZones BallastVolumeZones(ISeries<double> input , int legBars, int baseBars, bool useVolumeFilter, int volumeLookback, double volumeMultiple, int maxZones, int extendRightBars, int baseOpacity, bool showLabels, bool showBroken)
		{
			return indicator.BallastVolumeZones(input, legBars, baseBars, useVolumeFilter, volumeLookback, volumeMultiple, maxZones, extendRightBars, baseOpacity, showLabels, showBroken);
		}
	}
}

#endregion
