#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

// BallastStacker  (INDICATOR that PLACES orders -- keep it on Sim)
// ---------------------------------------------------------------------------
// Turns NinjaTrader's OWN Chart Trader buttons (Buy Mkt / Ask / Bid, Sell ...)
// into stacking buttons: it finds those native buttons, swallows their click,
// and submits its own entry + an INDEPENDENT OCO stop/target bracket. Each click
// = its own bracket that never merges.
//
// It uses whatever account the Chart Trader is currently on (read live at click
// time), so switching accounts needs no setting change. No strategy to enable.
//
// LIVE ORDER ENTRY FROM AN INDICATOR -- test on Sim only. Stop/target are in
// ticks. Watch New > NinjaScript Output for diagnostics.
// ---------------------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Indicators
{
	public class BallastStacker : Indicator
	{
		private enum PriceKind { None, Bid, Ask }

		private DependencyObject	chartTrader;
		private bool				hooked;
		private bool				hooking;
		private readonly List<Tuple<System.Windows.Controls.Button, MouseButtonEventHandler>> hookedButtons =
			new List<Tuple<System.Windows.Controls.Button, MouseButtonEventHandler>>();
		private readonly List<Account> subscribed = new List<Account>();
		private readonly HashSet<string> bracketed = new HashSet<string>();

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"Makes NinjaTrader's own Buy/Sell buttons place independent stacked brackets (per-click OCO).";
				Name		= "BallastStacker";
				Calculate	= Calculate.OnEachTick;
				IsOverlay	= true;
				DrawOnPricePanel			= false;
				IsSuspendedWhileInactive	= false;

				OrderQty	= 1;
				StopTicks	= 45;
				TargetTicks	= 60;
			}
			else if (State == State.Terminated)
			{
				Cleanup();
			}
		}

		protected override void OnBarUpdate()
		{
			if (!hooked && !hooking && ChartControl != null)
			{
				hooking = true;
				ChartControl.Dispatcher.InvokeAsync((Action)(() => AttachHooks()));
			}
		}

		private void AttachHooks()
		{
			try
			{
				SubscribeAll();   // hear fills on every account, so it follows account switches

				System.Windows.Window win = System.Windows.Window.GetWindow(ChartControl);
				DependencyObject ct = win == null ? null : FindByTypeName(win, "ChartTrader");
				if (ct == null)
				{
					Print("BallastStacker: Chart Trader not found -- open it on this chart (right-click > Chart Trader), then re-add me.");
					hooking = false;
					return;
				}
				chartTrader = ct;

				// DIAGNOSTIC PASS: map the Chart Trader's real buttons/labels so the
				// finder can be written correctly. Runs once, then stops.
				Print("===== BallastStacker: Chart Trader BUTTON MAP (send me everything between the ===== lines) =====");
				DumpButtons(ct, 0);
				Print("===== BallastStacker: END BUTTON MAP =====");

				Account cur = CurrentAccount();
				Print("BallastStacker: current Chart Trader account = " + (cur != null ? cur.Name : "COULD NOT READ IT"));

				hooked  = true;   // one diagnostic pass only -- no more retry spam
				hooking = false;
			}
			catch (Exception ex) { Print("BallastStacker AttachHooks error: " + ex.Message); hooking = false; }
		}

		private void HookOne(DependencyObject ct, string label, OrderAction action, OrderType type, PriceKind pk)
		{
			System.Windows.Controls.Button btn = FindButtonByContent(ct, label);
			if (btn == null) { Print("BallastStacker: native button '" + label + "' not found."); return; }

			MouseButtonEventHandler h = (s, e) =>
			{
				e.Handled = true;   // swallow the native order, run ours instead
				double price = pk == PriceKind.Bid ? GetBid() : pk == PriceKind.Ask ? GetAsk() : 0;
				HandleClick(action, type, price, label);
			};
			btn.AddHandler(System.Windows.Controls.Button.PreviewMouseLeftButtonDownEvent, h, true);
			hookedButtons.Add(new Tuple<System.Windows.Controls.Button, MouseButtonEventHandler>(btn, h));
			Print("BallastStacker: hooked '" + label + "'.");
		}

		private void HandleClick(OrderAction action, OrderType type, double price, string label)
		{
			try
			{
				Account acct = CurrentAccount();
				if (acct == null) { Print("BallastStacker: could not read the Chart Trader account for '" + label + "'."); return; }

				string unitId = Guid.NewGuid().ToString("N").Substring(0, 8);
				double limit  = type == OrderType.Limit ? price : 0;

				Order entry = acct.CreateOrder(Instrument, action, type, OrderEntry.Manual,
					TimeInForce.Gtc, OrderQty, limit, 0, string.Empty, "BSK_E_" + unitId,
					NinjaTrader.Core.Globals.MinDate, null);

				acct.Submit(new[] { entry });
				Print("BallastStacker: " + label + " -> " + acct.Name + " " + action + " " + type
					+ (limit > 0 ? " @ " + limit : "") + "  qty " + OrderQty + "  unit " + unitId);
			}
			catch (Exception ex) { Print("BallastStacker HandleClick error: " + ex.Message); }
		}

		// When one of OUR entries fills, drop its own independent OCO bracket on the
		// SAME account the fill happened on.
		private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
		{
			try
			{
				Execution ex = e.Execution;
				if (ex == null || ex.Order == null) return;
				string name = ex.Order.Name ?? string.Empty;
				if (!name.StartsWith("BSK_E_")) return;
				if (ex.Quantity <= 0) return;
				if (bracketed.Contains(name)) return;
				bracketed.Add(name);

				Account acct = ex.Order.Account;
				if (acct == null) return;

				string unitId = name.Substring("BSK_E_".Length);
				bool   wasBuy = ex.MarketPosition == MarketPosition.Long;
				double tick   = Instrument.MasterInstrument.TickSize;
				double fill   = ex.Price;

				OrderAction prot   = wasBuy ? OrderAction.Sell : OrderAction.Buy;
				double stopPrice   = wasBuy ? fill - StopTicks   * tick : fill + StopTicks   * tick;
				double targetPrice = wasBuy ? fill + TargetTicks * tick : fill - TargetTicks * tick;
				string oco         = "BSK_OCO_" + unitId;

				Order stop = acct.CreateOrder(Instrument, prot, OrderType.StopMarket, OrderEntry.Manual,
					TimeInForce.Gtc, OrderQty, 0, stopPrice, oco, "BSK_S_" + unitId, NinjaTrader.Core.Globals.MinDate, null);
				Order tgt  = acct.CreateOrder(Instrument, prot, OrderType.Limit, OrderEntry.Manual,
					TimeInForce.Gtc, OrderQty, targetPrice, 0, oco, "BSK_T_" + unitId, NinjaTrader.Core.Globals.MinDate, null);

				acct.Submit(new[] { stop, tgt });
				Print("BallastStacker: unit " + unitId + " filled @ " + fill + " on " + acct.Name
					+ " -> stop " + stopPrice + " / target " + targetPrice);
			}
			catch (Exception ex2) { Print("BallastStacker OnExecutionUpdate error: " + ex2.Message); }
		}

		// ── Account (read live from Chart Trader) ────────────────────────────
		private Account CurrentAccount()
		{
			// 1) Chart Trader's own Account property, via reflection (no hard type dep).
			try
			{
				if (chartTrader != null)
				{
					System.Reflection.PropertyInfo p = chartTrader.GetType().GetProperty("Account");
					if (p != null)
					{
						Account a = p.GetValue(chartTrader) as Account;
						if (a != null) return a;
					}
				}
			}
			catch { }

			// 2) Fallback: an account selector somewhere inside the Chart Trader.
			try { if (chartTrader != null) { Account a = FindAccountInTree(chartTrader); if (a != null) return a; } }
			catch { }

			return null;
		}

		private Account FindAccountInTree(DependencyObject root)
		{
			System.Windows.Controls.ComboBox combo = root as System.Windows.Controls.ComboBox;
			if (combo != null && combo.SelectedItem != null)
			{
				if (combo.SelectedItem is Account) return (Account)combo.SelectedItem;
				Account byName = AccountByName(combo.SelectedItem.ToString());
				if (byName != null) return byName;
			}
			int n = VisualTreeHelper.GetChildrenCount(root);
			for (int i = 0; i < n; i++)
			{
				Account a = FindAccountInTree(VisualTreeHelper.GetChild(root, i));
				if (a != null) return a;
			}
			return null;
		}

		private Account AccountByName(string name)
		{
			try { lock (Account.All) foreach (Account a in Account.All) if (a.Name == name) return a; }
			catch { }
			return null;
		}

		private void SubscribeAll()
		{
			try
			{
				lock (Account.All)
					foreach (Account a in Account.All)
						if (!subscribed.Contains(a)) { a.ExecutionUpdate += OnExecutionUpdate; subscribed.Add(a); }
				Print("BallastStacker: listening for fills on " + subscribed.Count + " account(s).");
			}
			catch (Exception ex) { Print("BallastStacker SubscribeAll error: " + ex.Message); }
		}

		private void Cleanup()
		{
			try { foreach (Account a in subscribed) a.ExecutionUpdate -= OnExecutionUpdate; } catch { }
			subscribed.Clear();

			if (ChartControl == null) return;
			ChartControl.Dispatcher.InvokeAsync((Action)(() =>
			{
				try
				{
					foreach (Tuple<System.Windows.Controls.Button, MouseButtonEventHandler> t in hookedButtons)
						t.Item1.RemoveHandler(System.Windows.Controls.Button.PreviewMouseLeftButtonDownEvent, t.Item2);
				}
				catch { }
				hookedButtons.Clear();
				hooked = false;
			}));
		}

		private double GetBid() { try { double b = GetCurrentBid(); return b > 0 ? b : Close[0]; } catch { return Close[0]; } }
		private double GetAsk() { try { double a = GetCurrentAsk(); return a > 0 ? a : Close[0]; } catch { return Close[0]; } }

		// ── Visual-tree helpers ──────────────────────────────────────────────
		private DependencyObject FindByTypeName(DependencyObject root, string contains)
		{
			if (root == null) return null;
			if (root.GetType().Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) return root;
			int n = VisualTreeHelper.GetChildrenCount(root);
			for (int i = 0; i < n; i++)
			{
				DependencyObject found = FindByTypeName(VisualTreeHelper.GetChild(root, i), contains);
				if (found != null) return found;
			}
			return null;
		}

		private System.Windows.Controls.Button FindButtonByContent(DependencyObject root, string contains)
		{
			System.Windows.Controls.Button b = root as System.Windows.Controls.Button;
			if (b != null && b.Content is string
				&& ((string)b.Content).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) return b;
			int n = VisualTreeHelper.GetChildrenCount(root);
			for (int i = 0; i < n; i++)
			{
				System.Windows.Controls.Button found = FindButtonByContent(VisualTreeHelper.GetChild(root, i), contains);
				if (found != null) return found;
			}
			return null;
		}

		// Prints anything that could be an order button, plus its real label.
		private void DumpButtons(DependencyObject root, int depth)
		{
			if (root == null) return;
			string type = root.GetType().Name;

			System.Windows.Controls.Primitives.ButtonBase bb = root as System.Windows.Controls.Primitives.ButtonBase;
			if (bb != null)
			{
				object c = bb.Content;
				string cs = c == null ? "null" : "(" + c.GetType().Name + ") \"" + c.ToString() + "\"";
				Print(new string(' ', depth) + type + "  Content=" + cs + "  name=\"" + bb.Name + "\"");
			}
			else
			{
				System.Windows.Controls.TextBlock tb = root as System.Windows.Controls.TextBlock;
				if (tb != null) Print(new string(' ', depth) + "TextBlock \"" + tb.Text + "\"");
				else if (type.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
					Print(new string(' ', depth) + type + "  (non-Button-base button-like)");
			}

			int n = VisualTreeHelper.GetChildrenCount(root);
			for (int i = 0; i < n; i++) DumpButtons(VisualTreeHelper.GetChild(root, i), depth + 1);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Order quantity", Order = 0, GroupName = "Stacker")]
		public int OrderQty { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Stop (ticks)", Order = 1, GroupName = "Stacker")]
		public int StopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Target (ticks)", Order = 2, GroupName = "Stacker")]
		public int TargetTicks { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BallastStacker[] cacheBallastStacker;
		public BallastStacker BallastStacker(int orderQty, int stopTicks, int targetTicks)
		{
			return BallastStacker(Input, orderQty, stopTicks, targetTicks);
		}

		public BallastStacker BallastStacker(ISeries<double> input, int orderQty, int stopTicks, int targetTicks)
		{
			if (cacheBallastStacker != null)
				for (int idx = 0; idx < cacheBallastStacker.Length; idx++)
					if (cacheBallastStacker[idx] != null && cacheBallastStacker[idx].OrderQty == orderQty && cacheBallastStacker[idx].StopTicks == stopTicks && cacheBallastStacker[idx].TargetTicks == targetTicks && cacheBallastStacker[idx].EqualsInput(input))
						return cacheBallastStacker[idx];
			return CacheIndicator<BallastStacker>(new BallastStacker(){ OrderQty = orderQty, StopTicks = stopTicks, TargetTicks = targetTicks }, input, ref cacheBallastStacker);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BallastStacker BallastStacker(int orderQty, int stopTicks, int targetTicks)
		{
			return indicator.BallastStacker(Input, orderQty, stopTicks, targetTicks);
		}

		public Indicators.BallastStacker BallastStacker(ISeries<double> input , int orderQty, int stopTicks, int targetTicks)
		{
			return indicator.BallastStacker(input, orderQty, stopTicks, targetTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BallastStacker BallastStacker(int orderQty, int stopTicks, int targetTicks)
		{
			return indicator.BallastStacker(Input, orderQty, stopTicks, targetTicks);
		}

		public Indicators.BallastStacker BallastStacker(ISeries<double> input , int orderQty, int stopTicks, int targetTicks)
		{
			return indicator.BallastStacker(input, orderQty, stopTicks, targetTicks);
		}
	}
}

#endregion
