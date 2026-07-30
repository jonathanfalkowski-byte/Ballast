// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastAddOn.cs
//
// Registers "Ballast" under the Control Center's "New" menu so the discipline
// window can be opened like any other NinjaTrader window.
//
// Install: put the Ballast folder in
//   Documents\NinjaTrader 8\bin\Custom\AddOns\
// then press F5 in the NinjaScript Editor to compile.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Windows;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class BallastAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem ballastMenuItem;
        private NTMenuItem existingMenuItemInControlCenter;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ballast";
                Description = "Discipline layer for prop futures traders - advisory only.";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            // Only attach to the Control Center.
            ControlCenter cc = window as ControlCenter;
            if (cc == null) return;

            existingMenuItemInControlCenter = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (existingMenuItemInControlCenter == null) return;

            ballastMenuItem = new NTMenuItem
            {
                Header = "Ballast",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };

            existingMenuItemInControlCenter.Items.Add(ballastMenuItem);
            ballastMenuItem.Click += OnMenuItemClick;
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (ballastMenuItem != null && window is ControlCenter)
            {
                if (existingMenuItemInControlCenter != null &&
                    existingMenuItemInControlCenter.Items.Contains(ballastMenuItem))
                {
                    existingMenuItemInControlCenter.Items.Remove(ballastMenuItem);
                }

                ballastMenuItem.Click -= OnMenuItemClick;
                ballastMenuItem = null;
            }
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new BallastWindow().Show()));
        }
    }
}
