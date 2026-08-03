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
// IT DRAWS IN ITS OWN PANEL, and that is the whole point.
//
// The first versions painted onto the price panel. On a real trading chart that
// is the most contested space on the screen: NinjaTrader lists every indicator's
// name across the top left, the instrument watermark sits top right, and a chart
// with eight studies on it has a wall of coloured text before Ballast draws a
// single character. Making the text bigger, redder or more opaque does not win
// that fight - it just adds one more thing to the pile.
//
// So it stops competing. Ballast gets a thin panel of its own beneath the price
// panel, the way volume does. Nothing else can ever draw there, so it can never
// be buried, and a trader always knows exactly where to look. Drag it taller or
// shorter like any other panel.
//
// DESIGN RULES, both deliberate:
//
//   1. Alarms fill the panel. A hard breaker paints the whole strip red. In a
//      space nothing else occupies, that is impossible to miss without needing
//      to shout over anything.
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
using System.ComponentModel.DataAnnotations;
using System.Reflection;
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
using BallastState        = Ballast.BallastState;

namespace NinjaTrader.NinjaScript.Indicators
{
    // The account dropdown that used to live here is gone. The chart already
    // knows which account it trades - it is in its own Chart Trader - and asking
    // the trader to state it a second time inside an indicator only created ways
    // for the two to disagree. Worse, someone who trades one chart against
    // several accounts had to remember to edit the indicator every time they
    // switched, and forgetting meant watching the wrong account's risk without
    // any sign that anything was wrong.

    public class BallastChartWarning : Indicator
    {
        /// <summary>
        /// Every brush here is FROZEN, and that is not a detail.
        ///
        /// NinjaTrader runs each chart on its own UI thread. A WPF brush belongs
        /// to the thread that created it, so a static brush built by whichever
        /// chart happened to load first cannot be touched by any other chart -
        /// it throws "the calling thread cannot access this object because a
        /// different thread owns it", and NinjaTrader swallows it as a failed
        /// draw. The indicator then paints perfectly on one chart and is
        /// silently, permanently blank on every other one, which is exactly what
        /// this looked like for several rounds.
        ///
        /// Freezing makes a brush immutable and therefore safe to share across
        /// threads. It costs nothing and it is the whole fix.
        /// </summary>
        private static Brush Frozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static readonly Brush Amber = Frozen(0xe3, 0xb3, 0x41);
        private static readonly Brush Ink   = Frozen(0xe8, 0xed, 0xf3);
        // Red is not declared here any more: an alert now fills the strip with
        // AlarmBack rather than tinting text on the chart background.
        private static readonly Brush Panel = Frozen(0x16, 0x1b, 0x22);
        private static readonly Brush Green = Frozen(0x3f, 0xb9, 0x50);

        /// <summary>Alarm colours. Solid, because the strip is ours to fill.</summary>
        private static readonly Brush AlarmBack = Frozen(0x8b, 0x14, 0x0c);
        private static readonly Brush AlarmInk  = Frozen(0xff, 0xf2, 0xf0);

        private const string Tag = "BallastWarning";

        // Repaints on a clock rather than on ticks. See StartClock().
        private System.Windows.Threading.DispatcherTimer clock;

        /// <summary>
        /// The account this chart's own Chart Trader is pointed at.
        ///
        /// This is the ONLY way the indicator learns which account it is about.
        /// The chart knows; ask the chart. Anything the trader could type here
        /// instead is a second copy of a fact that already exists, free to drift
        /// out of date the moment they trade the same chart on another account.
        ///
        /// Resolved on the UI thread in the clock tick and cached here, because
        /// walking up to the chart window is not safe from the NinjaScript
        /// thread. Re-read every tick, so switching the Chart Trader account
        /// follows within a second.
        /// </summary>
        private volatile string chartAccount = "";

        [Display(Name = "Text size", Order = 2, GroupName = "Ballast")]
        public int TextSize { get; set; }

        [Display(Name = "Also show when only cautioning", Order = 3, GroupName = "Ballast",
                 Description = "Off means only genuine alerts appear - stops, lockouts, tilt windows.")]
        public bool ShowCautions { get; set; }

        [Display(Name = "Show the account name in the count", Order = 4, GroupName = "Ballast",
                 Description = "Turn off if this chart only ever trades one account and the name is just noise.")]
        public bool ShowAccountName { get; set; }

        /// <summary>
        /// Where on the chart to draw.
        ///
        /// It was pinned to the top left, which is exactly where NinjaTrader
        /// prints the name of every indicator on the chart. On a chart running
        /// half a dozen of them the Ballast banner was one line of coloured text
        /// among twenty - unreadable, and worse than useless, because a warning
        /// you have to hunt for is one you will not see in the second that counts.
        /// </summary>
        [Display(Name = "Where in the Ballast panel", Order = 5, GroupName = "Ballast",
                 Description = "Where the text sits inside Ballast's own panel. Nothing else draws there, so this is purely taste.")]
        public TextPosition Where { get; set; }

        [Display(Name = "Centre alarms in the panel", Order = 6, GroupName = "Ballast",
                 Description = "A hard stop is centred and fills the strip. The quiet count stays where you put it.")]
        public bool CentreAlarms { get; set; }

        /// <summary>
        /// What NinjaTrader prints as this indicator's label on the panel.
        ///
        /// By default that is the class name with every parameter after it -
        /// "BallastChartWarning(NQ SEP26 (50 AlgoBars ROCK))" - forty-odd
        /// characters of nothing useful, printed in the top left of Ballast's own
        /// strip, directly into the line Ballast is trying to make readable. The
        /// warning then had to start somewhere to the right of it and the whole
        /// thing read as a jumble.
        ///
        /// One word instead. It still says which indicator this is, which is the
        /// only job the label had.
        /// </summary>
        public override string DisplayName { get { return "Ballast"; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Shows the Ballast warning for one account across the top of this chart.";
                Name = "BallastChartWarning";
                Calculate = Calculate.OnEachTick;

                // Its own panel, not the price panel. This is the fix for being
                // buried - see the header.
                IsOverlay = false;
                DrawOnPricePanel = false;

                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;

                // An invisible flat plot. NinjaTrader needs something to hang a
                // panel on; this gives it one without drawing anything, so the
                // strip is Ballast's and stays empty until it has something to
                // say.
                AddPlot(System.Windows.Media.Brushes.Transparent, "Ballast");

                TextSize = 22;
                ShowCautions = true;
                ShowAccountName = true;
                // Top RIGHT of the panel. NinjaTrader prints the indicator's own
                // name and its copyright line down the panel's top left, so that
                // corner is the one corner of an otherwise empty strip that is
                // already taken.
                Where = TextPosition.TopRight;
                CentreAlarms = true;
            }
            else if (State == State.Historical || State == State.Realtime)
            {
                // The clock used to be started from OnBarUpdate, which means it
                // only ever started on a chart that was ticking. Put this on a
                // slow bar type - a volume chart, a Renko chart waiting for a bar
                // to complete - and OnBarUpdate can be minutes apart or, on a
                // chart loaded outside session hours, never fire at all. The
                // indicator then drew nothing, on a chart where it was correctly
                // installed and configured, forever.
                StartClock();
            }
            else if (State == State.Terminated)
            {
                StopClock();
            }
        }

        /// <summary>
        /// The heartbeat that makes this work on every chart, not most of them.
        ///
        /// Everything used to hang off OnBarUpdate: it started the repaint clock
        /// and did the first paint. On a normal time or volume chart that is
        /// fine, because ticks arrive constantly. On a third-party bar type - a
        /// Renko, an AlgoBars ROCK chart - OnBarUpdate can be minutes apart, and
        /// if ChartControl was not yet available at the state change the clock
        /// never started at all. The panel then sat empty forever, on a chart
        /// where the indicator was correctly installed. That is exactly what
        /// "installed on both, only paints on one" looked like.
        ///
        /// OnRender is called by the chart every time it repaints, whatever the
        /// bar type is doing, so it is the one callback that can be relied on.
        /// It does no drawing itself - it just makes sure the clock is running,
        /// and the clock marshals the real painting onto the NinjaScript thread.
        /// </summary>
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            try { base.OnRender(chartControl, chartScale); }
            catch { }

            try { StartClock(); }
            catch { }
        }

        protected override void OnBarUpdate()
        {
            // Separate nets. If setting the plot ever fails, that must not also
            // stop the painting - which is the part that matters.
            try { Values[0][0] = 0; } catch { }
            try { StartClock(); } catch { }
            try { Paint(); }
            catch (Exception ex) { Complain("paint from bar update", ex); }
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

        /// <summary>
        /// Walk from this indicator up to its chart window and read the account
        /// its Chart Trader is set to. Reflection on purpose: the chart-trader
        /// types are not part of the indicator API surface, and a hard reference
        /// that breaks in a future NinjaTrader release would stop the whole of
        /// bin\Custom compiling, not just this file. Any failure here simply
        /// means falling back to the other ways of working out the account.
        ///
        /// MUST be called on the UI thread.
        /// </summary>
        private string ReadChartTraderAccount()
        {
            try
            {
                if (ChartControl == null) return "";

                object chart = System.Windows.Window.GetWindow(ChartControl);
                if (chart == null) return "";

                PropertyInfo traderProp = chart.GetType().GetProperty("ChartTrader");
                if (traderProp == null) return "";

                object trader = traderProp.GetValue(chart, null);
                if (trader == null) return "";

                PropertyInfo accountProp = trader.GetType().GetProperty("Account");
                if (accountProp == null) return "";

                object account = accountProp.GetValue(trader, null);
                if (account == null) return "";

                PropertyInfo nameProp = account.GetType().GetProperty("Name");
                if (nameProp == null) return "";

                string name = nameProp.GetValue(account, null) as string;
                return name ?? "";
            }
            catch { return ""; }
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            // Draw objects must be created on the NinjaScript thread, not on the
            // chart's UI thread that this timer runs on. Calling Draw directly
            // from here worked on some charts and silently did nothing on others,
            // which is exactly the "installed on both, only paints on one"
            // behaviour that this looked like. TriggerCustomEvent marshals the
            // callback onto the right thread, which is what it is for.
            // On the UI thread here, which is the only place it is safe to walk
            // up to the chart window.
            try { chartAccount = ReadChartTraderAccount(); } catch { }

            try
            {
                TriggerCustomEvent(delegate(object state)
                {
                    try { Paint(); ForceRefresh(); }
                    catch (Exception ex) { Complain("paint from clock", ex); }
                }, null);
            }
            catch
            {
                // If the custom event cannot be raised - the indicator is being
                // torn down, say - a missed repaint is nothing. The next tick
                // covers it.
            }
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

        /// <summary>
        /// Every line Ballast paints goes through here.
        ///
        /// The status messages used to be drawn with their own hard-coded 14 and
        /// 16 point fonts on a see-through background, so they came out smaller
        /// and fainter than everything else and were unreadable on a busy chart -
        /// while the setting called "Text size" appeared to do nothing to them.
        /// One helper, one size scale, solid backgrounds throughout.
        /// </summary>
        /// <summary>
        /// Say so, once, in NinjaTrader's own log.
        ///
        /// Every failure in here used to be swallowed by a bare catch, on the
        /// principle that an indicator must never take a chart down. That is
        /// right, but it also meant a silently blank panel gave no clue anywhere
        /// as to why - the log was clean while the thing plainly did not work.
        /// Once per distinct message, so a failure on every repaint cannot fill
        /// the log.
        /// </summary>
        private string lastComplaint = "";

        private void Complain(string where, Exception ex)
        {
            try
            {
                string msg = "Ballast chart indicator: " + where + " failed - "
                           + (ex == null ? "unknown" : ex.Message);
                if (msg == lastComplaint) return;
                lastComplaint = msg;
                Log(msg, LogLevel.Warning);
            }
            catch { }
        }

        private void Say(string text, Brush ink, Brush back, double scale, TextPosition where)
        {
            int baseSize = TextSize < 8 ? 8 : (TextSize > 72 ? 72 : TextSize);
            int size = (int)Math.Round(baseSize * scale);
            if (size < 11) size = 11;
            if (size > 72) size = 72;

            try
            {
                Draw.TextFixed(this, Tag, text, where, ink,
                               new SimpleFont("Arial", size), back, back, 100);
            }
            catch (Exception ex) { Complain("drawing \"" + text + "\"", ex); }
        }

        private void Paint()
        {
            // Whichever account this chart's own Chart Trader is set to, read
            // fresh every second. Switch the chart to another account and Ballast
            // follows within a tick, with nothing to configure and nothing that
            // can be left pointing at yesterday's account.
            string account = chartAccount;

            // A chart with no Chart Trader at all still works if there is only
            // one thing it could possibly mean.
            if (string.IsNullOrEmpty(account))
            {
                List<string> known = BallastState.KnownAccounts();
                if (known.Count == 1) account = known[0];
            }

            if (string.IsNullOrEmpty(account))
            {
                Say("BALLAST: PICK AN ACCOUNT IN THIS CHART'S CHART TRADER",
                    Amber, Panel, 0.7, Where);
                return;
            }

            AccountState st = BallastState.Get(account, DateTime.Now);

            // Window closed, or the name does not match an account it is watching.
            // Say which, rather than drawing nothing and leaving the trader to
            // wonder whether it is working or simply has nothing to report.
            if (st == null)
            {
                List<string> known = BallastState.KnownAccounts();

                // Now that the account can come from the chart rather than from
                // something the trader typed, "not watching" needs to say which
                // account it means and where that name came from - otherwise it
                // reads as a fault in the indicator rather than as a ticked box
                // missing over in Setup.
                string why;
                if (known.Count == 0)
                {
                    why = "BALLAST WINDOW IS NOT OPEN";
                }
                else if (known.Contains(account))
                {
                    why = "BALLAST DATA HAS GONE STALE";
                }
                else
                {
                    why = "BALLAST IS NOT WATCHING " + account.ToUpperInvariant()
                        + "  (THIS CHART'S ACCOUNT) - TICK IT IN SETUP";
                }

                Say(why, Amber, Panel, 0.7, Where);
                return;
            }

            // Calm states are deliberately silent - see the header. A tiny green
            // tick confirms it is alive without becoming wallpaper.
            // A hard breaker is never silent, whatever the caution setting says
            // and whatever else has or has not been published for this account.
            // This is the case the whole indicator exists for.
            if (!st.Locked && (st.Urgency <= 0 || (st.Urgency == 1 && !ShowCautions)))
            {
                // Not "BALLAST OK". That said nothing, changed for no reason a
                // trader could see, and made it impossible to tell a working
                // indicator from a stuck one - which is exactly how it read when
                // an account's rules were edited and the chart carried on saying
                // the same two words.
                //
                // The count instead: trades taken, losses in a row, what is left
                // of today's budget, and the room to the floor. Small and dim, so
                // it stays a thing you glance at rather than react to, but it is
                // real information and it moves when the account does.
                string count = BallastState.ChartCount(st, ShowAccountName ? account : null);
                if (count.Length == 0) count = "BALLAST WATCHING " + account.ToUpperInvariant();

                Say(count, st.DailyPnl < 0 ? Ink : Green, Panel, 0.7, Where);
                return;
            }

            string text = BallastState.ChartBanner(st);
            if (text.Length == 0) { RemoveDrawObject(Tag); return; }

            // "Can lose" alongside "stop" reads as a budget to spend. Once the
            // account is past a hard line there is no number to offer.
            if (st.HasCushion && !st.Locked)
                text += "        CAN LOSE " + Money(st.CanLose);

            // An alarm fills Ballast's own strip: solid background, no
            // transparency, centred. There is nothing underneath it to blend
            // into, which is the entire reason for having a panel.
            bool alarm = BallastState.IsAlarm(st);
            TextPosition where = (CentreAlarms && alarm) ? TextPosition.Center : Where;

            Say(text,
                alarm ? AlarmInk : Amber,
                alarm ? AlarmBack : Panel,
                st.Locked ? 1.4 : 1.0,
                where);
        }

        private static string Money(double v)
        {
            double r = Math.Round(v);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0");
        }
    }
}
