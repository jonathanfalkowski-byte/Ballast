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
        private static readonly Brush PanelBrush = Frozen(0x16, 0x1b, 0x22);
        private static readonly Brush Green = Frozen(0x3f, 0xb9, 0x50);

        /// <summary>Alarm colours. Solid, because the strip is ours to fill.</summary>
        private static readonly Brush AlarmBack = Frozen(0x8b, 0x14, 0x0c);
        private static readonly Brush AlarmInk  = Frozen(0xff, 0xf2, 0xf0);

        private const string Tag = "BallastWarning";

        /// <summary>
        /// Font scale for the quiet count. Deliberately below the alert size:
        /// this line is six figures long and has to fit the panel on one row at
        /// any zoom a trader actually uses.
        /// </summary>
        private const double CountScale = 0.7;

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

        [Display(Name = "Grey out Chart Trader buy/sell when this account is stopped", Order = 5,
                 GroupName = "Ballast",
                 Description = "Turns off the ENTRY buttons on this chart while a hard breaker is in "
                             + "force. Close, Reverse, Flatten and Cancel always stay live. Typing the "
                             + "sentence into the Ballast wall brings them back. This is a speed bump, "
                             + "not a lock - the SuperDOM, order ticket, hotkeys and your firm's web "
                             + "platform all still work.")]
        public bool BlockOrderEntry { get; set; }

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

                // Bar close, not every tick.
                //
                // Nothing here is measured from price. The panel is repainted by
                // its own one-second timer - see StartClock - so asking to be
                // woken on every tick of NQ bought nothing at all and cost a
                // redraw at tick rate on the chart's UI thread. On a fast
                // instrument in a busy morning that is thousands of pointless
                // repaints a minute, on the same thread the chart uses to draw
                // price, and the backlog only grows.
                Calculate = Calculate.OnBarClose;

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

                // Off by default, and it should stay off until a trader
                // deliberately asks for it. Everything else in Ballast only
                // reads the platform; this is the one thing that reaches in and
                // changes it, and that is not a decision to make on somebody's
                // behalf by shipping it switched on.
                BlockOrderEntry = false;
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

                // Never leave a chart with dead buttons because Ballast was
                // removed, reloaded or crashed. Whatever else happens, the
                // platform goes back exactly as it was found.
                try
                {
                    if (entryDisabled && ChartControl != null)
                        ChartControl.Dispatcher.InvokeAsync(new Action(delegate
                        {
                            try { SetEntryButtons(true); } catch { }
                        }));
                }
                catch { }
                entryDisabled = false;
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

            // No painting from here any more. The clock does it once a second,
            // which is both often enough for a discipline warning and the only
            // rate that does not scale with how fast the market happens to be
            // trading. Painting here as well meant the busiest moments - exactly
            // when the chart most needs its thread - carried the most redundant
            // work.
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

        // ── Turning the order buttons off ───────────────────────────────────
        //
        // "is there a way to turn off the buy and sell buttons if i dont want to
        // trade that account....to avoid me taking another trade ?"
        //
        // Three things this deliberately is NOT.
        //
        // It is not a lock. Chart Trader is one way into a position out of many -
        // the SuperDOM, the order ticket, hotkeys, ATM strategies and the firm's
        // own web platform are all still there and all still work. This is a
        // speed bump on the door he actually uses, and it says so rather than
        // letting him believe he is sealed in.
        //
        // It is not permanent. He asked for it to come back when he types the
        // sentence into the wall, and it does - the buttons follow the override,
        // even though the chart banner deliberately does not.
        //
        // And it never touches the way OUT. Close, Reverse, Flatten and Cancel
        // stay live at all times. Disabling the exit on a locked account with a
        // position on would be the single most dangerous thing this software
        // could do, and it is the exact case where the lock is most likely to be
        // in force.
        private bool entryDisabled;
        private int entryButtonsFound = -1;

        private static bool IsEntryButton(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().ToUpperInvariant();

            // The way out is never touched, whatever it is called.
            if (t.IndexOf("CLOSE", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("REVERSE", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("FLATTEN", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("CANCEL", StringComparison.Ordinal) >= 0) return false;
            if (t.IndexOf("EXIT", StringComparison.Ordinal) >= 0) return false;

            return t.StartsWith("BUY", StringComparison.Ordinal)
                || t.StartsWith("SELL", StringComparison.Ordinal);
        }

        private static string ButtonText(System.Windows.Controls.Button b)
        {
            try
            {
                if (b == null) return "";
                string byName = b.Name ?? "";
                object c = b.Content;
                string byContent = c == null ? "" : c.ToString();
                return byContent.Length > 0 ? byContent : byName;
            }
            catch { return ""; }
        }

        private static void CollectButtons(System.Windows.DependencyObject root,
                                           List<System.Windows.Controls.Button> into, int depth)
        {
            if (root == null || into == null || depth > 24) return;
            try
            {
                int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < n; i++)
                {
                    System.Windows.DependencyObject child =
                        System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    System.Windows.Controls.Button b = child as System.Windows.Controls.Button;
                    if (b != null) into.Add(b);
                    CollectButtons(child, into, depth + 1);
                }
            }
            catch { }
        }

        /// <summary>
        /// Enable or disable this chart's entry buttons. UI thread only.
        /// Returns how many it actually changed, or -1 if it could not look.
        /// </summary>
        private int SetEntryButtons(bool enabled)
        {
            try
            {
                if (ChartControl == null) return -1;

                System.Windows.DependencyObject chart = System.Windows.Window.GetWindow(ChartControl);
                if (chart == null) return -1;

                List<System.Windows.Controls.Button> buttons =
                    new List<System.Windows.Controls.Button>();
                CollectButtons(chart, buttons, 0);

                int touched = 0;
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (!IsEntryButton(ButtonText(buttons[i]))) continue;
                    buttons[i].IsEnabled = enabled;
                    touched++;
                }
                return touched;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Keep the buttons in step with the published state. Called from the
        /// clock tick, which already runs on this chart's UI thread.
        ///
        /// Only acts on a CHANGE, so the ordinary case costs one bool comparison
        /// a second rather than a visual-tree walk.
        /// </summary>
        private void SyncEntryButtons()
        {
            bool want;
            try
            {
                string account = chartAccount;
                if (string.IsNullOrEmpty(account))
                {
                    List<string> known = BallastState.KnownAccounts();
                    if (known.Count == 1) account = known[0];
                }

                AccountState st = string.IsNullOrEmpty(account)
                    ? null : BallastState.Get(account, DateTime.Now);

                // No state is not a reason to disable anything. A closed Ballast
                // window or a stale feed must always leave the platform as it
                // found it.
                want = BlockOrderEntry && st != null && st.OrderEntryBlocked;
            }
            catch { want = false; }

            if (want == entryDisabled && entryButtonsFound >= 0) return;

            int touched = SetEntryButtons(!want);
            entryDisabled = want;
            entryButtonsFound = touched;
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

            // Still on the UI thread, which is the only place the chart's own
            // controls can be touched.
            try { SyncEntryButtons(); } catch (Exception ex) { Complain("order buttons", ex); }

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

        /// <summary>What was last drawn, so an unchanged panel is not redrawn.</summary>
        private string lastSaid = null;

        private void Say(string text, Brush ink, Brush back, double scale, TextPosition where)
        {
            int baseSize = TextSize < 8 ? 8 : (TextSize > 72 ? 72 : TextSize);
            int size = (int)Math.Round(baseSize * scale);
            if (size < 11) size = 11;
            if (size > 72) size = 72;

            // Redraw only when something actually changed.
            //
            // Draw.TextFixed is not free: it goes through NinjaTrader's draw
            // object machinery and invalidates the chart. This panel says the
            // same sentence for minutes at a time - a trade count and a cushion
            // do not move on every tick - so almost every repaint was replacing
            // a string with an identical copy of itself and asking the chart to
            // redraw for it.
            //
            // The key covers everything that can change what is on screen. Miss
            // one and the panel goes stale, which is worse than slow: a stale
            // discipline warning is a lie told quietly.
            string key = text + "|" + size + "|" + where
                       + "|" + (ink == null ? "" : ink.ToString())
                       + "|" + (back == null ? "" : back.ToString());

            if (key == lastSaid) return;
            lastSaid = key;

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
                    Amber, PanelBrush, 0.7, Where);
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

                Say(why, Amber, PanelBrush, 0.7, Where);
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

                // Colour by how close the account is to its own lines, not by
                // whether the day happens to be red. "Down three dollars" and
                // "two of three losses with $374 of a $3,000 budget left" were
                // the same shade of white, and the second one is the whole
                // reason this line exists.
                int close = BallastState.CountUrgency(st);

                // Colour and background carry the warning. The SIZE does not.
                //
                // The fourth argument to Say is a font scale, not an opacity, and
                // raising it to 1.0 for a warning made this line 43% wider than
                // the panel - so it wrapped, and the second half landed on top of
                // NinjaTrader's own copyright line in the corner. A warning that
                // makes the chart look broken is a warning that gets turned off.
                //
                // At a line the strip is filled the way a real alarm is; getting
                // close is amber on the usual background. Both stay at the size
                // that fits.
                Say(count,
                    close >= 2 ? AlarmInk : close == 1 ? Amber
                        : st.DailyPnl < 0 ? Ink : Green,
                    close >= 2 ? AlarmBack : PanelBrush,
                    CountScale,
                    Where);
                return;
            }

            string text = BallastState.ChartBanner(st);

            // If the buttons were asked for and could not be turned off, that
            // has to be on the chart in the same breath as the stop.
            //
            // A trader who has switched this on will glance at a red banner and
            // assume the door is shut. If the visual-tree walk found nothing -
            // a NinjaTrader update moved the controls, Chart Trader is not open
            // on this chart - then the door is wide open and he is the last
            // person who should be finding that out by placing an order. A
            // safety feature that fails quietly is worse than not having it.
            if (BlockOrderEntry && st.OrderEntryBlocked && entryButtonsFound <= 0)
                text = text + "   (COULD NOT DISABLE THE BUY/SELL BUTTONS ON THIS CHART)";

            if (text.Length == 0) { lastSaid = null; RemoveDrawObject(Tag); return; }

            // "Can lose" alongside "stop" reads as a budget to spend. Once the
            // account is past a hard line there is no number to offer.
            //
            // Named the same way everywhere else names it. This figure is the
            // distance to the account ENDING, not today's budget, and the two
            // sat on adjacent lines under different words - "CAN LOSE" here,
            // "TO THE FLOOR" on the count line and in the window - which invited
            // exactly the mix-up the count line has just been fixed for.
            if (st.HasCushion && !st.Locked)
                text += "      " + Money(st.CanLose) + " TO FLOOR";

            // An alarm fills Ballast's own strip: solid background, no
            // transparency, centred. There is nothing underneath it to blend
            // into, which is the entire reason for having a panel.
            bool alarm = BallastState.IsAlarm(st);
            TextPosition where = (CentreAlarms && alarm) ? TextPosition.Center : Where;

            Say(text,
                alarm ? AlarmInk : Amber,
                alarm ? AlarmBack : PanelBrush,
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
