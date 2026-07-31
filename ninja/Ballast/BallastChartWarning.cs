// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastChartWarning.cs   (INDICATOR, not part of the AddOn)
//
// Puts the warning where the trader's eyes already are.
//
// The Ballast window is honest and useful and sitting off to one side, which is
// exactly where nobody looks at the moment they are about to revenge trade. This
// draws the same message across the top of the chart itself, in large letters,
// in the same colours.
//
// Add it like any indicator: right-click a chart, Indicators, BallastChartWarning,
// then set "Ballast account" to the account that chart trades.
//
// DESIGN RULES, both deliberate:
//
//   1. It only ever draws something ACTIONABLE. No "all clear" banner. A message
//      that appears constantly is one a trader learns to see through, and then it
//      is worth nothing on the day it matters.
//
//   2. It draws nothing at all if the Ballast window is closed or its data has
//      gone stale. A chart confidently showing an hour-old "you are fine" would
//      be worse than a blank chart.
//
// IF THIS FILE WILL NOT COMPILE: delete it. NinjaTrader builds every file in
// bin\Custom together, so one bad file stops everything - but nothing else in
// Ballast depends on this one, and the window works perfectly without it.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;              // SimpleFont, TextPosition
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;   // Draw
using Ballast;

// Pin the Ballast types explicitly. NinjaTrader.Cbi is a large namespace and an
// indicator imports most of NinjaTrader at once - this is the same collision
// that took the whole add-on down over JournalEntry, and it is cheaper to
// prevent than to diagnose from a screenshot.
using AccountState        = Ballast.AccountState;
using NaturalNameComparer = Ballast.NaturalNameComparer;
using BallastState        = Ballast.BallastState;

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Turns the account box into a dropdown.
    ///
    /// It was a free-text field, which is a setup step whose only possible
    /// outcomes are "typed correctly" and "typed wrong and silently shows
    /// nothing". The list is offered rather than enforced: a trader may add this
    /// indicator before ticking the account in Ballast, and a dropdown that
    /// refused anything it had not seen yet would be worse than the typing.
    /// </summary>
    public class BallastAccountConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }

        /// <summary>False on purpose - a name can still be typed in.</summary>
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> names = new List<string>();

            try
            {
                // Accounts Ballast is actually watching. These are the only ones
                // that can produce a warning, so they come first.
                List<string> watched = BallastState.KnownAccounts();
                for (int i = 0; i < watched.Count; i++)
                    if (!names.Contains(watched[i])) names.Add(watched[i]);
            }
            catch { }

            try
            {
                // Then everything NinjaTrader knows about, so the indicator can be
                // set up before Ballast has been pointed at the account.
                lock (Account.All)
                {
                    foreach (Account a in Account.All)
                        if (a != null && !string.IsNullOrEmpty(a.Name) && !names.Contains(a.Name))
                            names.Add(a.Name);
                }
            }
            catch { }

            try { names.Sort(NaturalNameComparer.Instance); } catch { }

            return new StandardValuesCollection(names);
        }
    }

    public class BallastChartWarning : Indicator
    {
        private static readonly Brush Red   = new SolidColorBrush(Color.FromRgb(0xf4, 0x52, 0x3b));
        private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xe3, 0xb3, 0x41));
        private static readonly Brush Ink   = new SolidColorBrush(Color.FromRgb(0xe8, 0xed, 0xf3));
        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x16, 0x1b, 0x22));
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));

        private const string Tag = "BallastWarning";

        // Repaints on a clock rather than on ticks. See StartClock().
        private System.Windows.Threading.DispatcherTimer clock;

        [TypeConverter(typeof(BallastAccountConverter))]
        [Display(Name = "Ballast account", Order = 1, GroupName = "Ballast",
                 Description = "The account this chart trades. Pick from the list, or leave blank if Ballast is watching only one.")]
        public string BallastAccount { get; set; }

        [Display(Name = "Text size", Order = 2, GroupName = "Ballast")]
        public int TextSize { get; set; }

        [Display(Name = "Also show when only cautioning", Order = 3, GroupName = "Ballast",
                 Description = "Off means only genuine alerts appear - stops, lockouts, tilt windows.")]
        public bool ShowCautions { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Shows the Ballast warning for one account across the top of this chart.";
                Name = "BallastChartWarning";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;

                BallastAccount = "";
                TextSize = 22;
                ShowCautions = true;
            }
            else if (State == State.Terminated)
            {
                StopClock();
            }
        }

        protected override void OnBarUpdate()
        {
            try { StartClock(); Paint(); }
            catch { /* an indicator must never take a chart down */ }
        }

        /// <summary>
        /// Repaint once a second, on a timer, regardless of ticks.
        ///
        /// The first version only painted from OnBarUpdate, which fires when the
        /// market trades. On a quiet instrument, outside RTH, or on a volume chart
        /// waiting for a bar to fill, that can be minutes apart - and a discipline
        /// warning that only appears when the market happens to tick is worthless
        /// precisely in the still moment before someone clicks Buy.
        ///
        /// It also means the cooldown countdown actually counts down.
        /// </summary>
        private void StartClock()
        {
            if (clock != null) return;
            if (ChartControl == null) return;

            try
            {
                ChartControl.Dispatcher.InvokeAsync(new Action(delegate
                {
                    try
                    {
                        if (clock != null) return;
                        clock = new System.Windows.Threading.DispatcherTimer();
                        clock.Interval = TimeSpan.FromSeconds(1);
                        clock.Tick += OnClockTick;
                        clock.Start();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            try { Paint(); ForceRefresh(); }
            catch { }
        }

        private void StopClock()
        {
            try
            {
                if (clock == null) return;
                clock.Stop();
                clock.Tick -= OnClockTick;
                clock = null;
            }
            catch { }
        }

        private void Paint()
        {
            string account = BallastAccount;

            // Blank, but Ballast is watching exactly one account - use it. Asking
            // someone to retype a name the tool already knows is a setup step
            // that exists only to be got wrong.
            if (string.IsNullOrEmpty(account))
            {
                List<string> known = BallastState.KnownAccounts();
                if (known.Count == 1) account = known[0];
            }

            if (string.IsNullOrEmpty(account))
            {
                // Loud on purpose. The quiet version of this message was invisible
                // among a chart's worth of indicator labels.
                Draw.TextFixed(this, Tag,
                    "BALLAST: SET \"BALLAST ACCOUNT\" IN THIS INDICATOR'S SETTINGS",
                    TextPosition.TopLeft, Amber, new SimpleFont("Arial", 16), Panel, Amber, 80);
                return;
            }

            AccountState st = BallastState.Get(account, DateTime.Now);

            // Window closed, or the name does not match an account it is watching.
            // Say which, rather than drawing nothing and leaving the trader to
            // wonder whether it is working or simply has nothing to report.
            if (st == null)
            {
                List<string> known = BallastState.KnownAccounts();

                string why = known.Count == 0
                    ? "BALLAST WINDOW IS NOT OPEN"
                    : (known.Contains(account)
                        ? "BALLAST DATA HAS GONE STALE"
                        : "BALLAST IS NOT WATCHING " + account.ToUpperInvariant());

                Draw.TextFixed(this, Tag, why, TextPosition.TopLeft,
                               Amber, new SimpleFont("Arial", 14), Panel, Amber, 70);
                return;
            }

            // Calm states are deliberately silent - see the header. A tiny green
            // tick confirms it is alive without becoming wallpaper.
            // A hard breaker is never silent, whatever the caution setting says
            // and whatever else has or has not been published for this account.
            // This is the case the whole indicator exists for.
            if (!st.Locked && (st.Urgency <= 0 || (st.Urgency == 1 && !ShowCautions)))
            {
                Draw.TextFixed(this, Tag, "BALLAST OK", TextPosition.TopLeft,
                               Green, new SimpleFont("Arial", 10), Panel, Panel, 25);
                return;
            }

            string text = BallastState.ChartBanner(st);
            if (text.Length == 0) { RemoveDrawObject(Tag); return; }

            // "Can lose" alongside "stop" reads as a budget to spend. Once the
            // account is past a hard line there is no number to offer.
            if (st.HasCushion && !st.Locked)
                text += "        CAN LOSE " + Money(st.CanLose);

            Brush colour = BallastState.IsAlarm(st) ? Red : Amber;
            int size = TextSize < 8 ? 8 : (TextSize > 72 ? 72 : TextSize);

            // Bigger when it matters, because he asked for it "right at the top
            // of the chart too in big letters" and this is the moment he meant.
            if (st.Locked) size = (int)Math.Round(size * 1.4);
            if (size > 72) size = 72;

            Draw.TextFixed(this, Tag, text, TextPosition.TopLeft,
                           colour, new SimpleFont("Arial", size), Panel, colour, 70);
        }

        private static string Money(double v)
        {
            double r = Math.Round(v);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0");
        }
    }
}
