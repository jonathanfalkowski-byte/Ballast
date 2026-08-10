// ─────────────────────────────────────────────────────────────────────────────
// Ballast — DisciplineEngine.cs
//
// Pure C# port of the tested TypeScript discipline engine. NO NinjaTrader
// dependencies live in this file on purpose: it can be compiled and unit-tested
// on its own, and the NinjaTrader AddOn simply feeds it live account state.
//
// Targets the conservative C# subset NinjaTrader 8 (.NET Framework 4.8) accepts —
// no records, no switch expressions, no nullable reference types.
//
// Philosophy (unchanged from the web version): every recommendation traces to an
// explicit signal. No hidden magic. A trader has to trust this in the tilt moment.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballast
{
    public enum Severity { Low, Medium, High }

    public enum DisciplineAction
    {
        Trade,          // conditions clean
        SizeDown,       // proceed smaller
        Cooldown,       // hands off, tilt window
        ProtectGreen,   // you're up — bank it or free-roll
        StopForDay,     // done
        Lockout,        // hit the daily loss limit
        None,           // nothing to act on
        CheckSetup      // the settings do not describe this account
    }

    public enum Urgency { Calm, Caution, Alert }

    public enum DrawdownType { Intraday, EndOfDay }

    public class RiskSignal
    {
        public string Key;
        public Severity Severity;
        public string Summary;

        public RiskSignal(string key, Severity severity, string summary)
        {
            Key = key;
            Severity = severity;
            Summary = summary;
        }
    }

    /// <summary>Everything the engine needs to decide, flat and explicit.</summary>
    public class DisciplineInput
    {
        public int LossStreak;
        public int TradesToday;
        public double DailyPnl;
        public double PeakDailyPnl;

        public double DailyLossLimit;
        public double DailyTarget;
        public int MaxLossesBeforeStop;
        public int MaxTrades;
        public int MaxContracts;
        public int OpenContracts;

        public double CushionToFloor;
        public double FloorLevel;        // the actual dollar level the account dies at
        public double CurrentEquity;
        public bool FloorLocked;         // true once the drawdown has stopped trailing
        public bool FirmFloorProviderConfirmed; // threshold came directly from the account provider
        public int BaseMaxContracts;     // size before the drawdown throttle
        public bool SizeThrottled;       // true when the throttle has cut the advised size

        /// <summary>The drawdown this account was set up with, needed to tell a
        /// blown account from a misconfigured one.</summary>
        public double TrailingDrawdown;

        /// <summary>
        /// True when the balance and the configured size cannot both be
        /// describing the same account.
        ///
        /// A funded account cannot be far below its floor. The firm closes it the
        /// moment it touches, so the worst a real account can be is a little past
        /// - a slipped stop, a gap through the level. An account sitting tens of
        /// thousands below a floor it supposedly breached weeks ago is not a dead
        /// account; it is a live one wearing somebody else's numbers, which is
        /// what happens when a 100K sim gets set up as a 150K prop account.
        ///
        /// Two conditions, both required, because either alone is wrong. More
        /// than TWICE the drawdown below the starting balance is arithmetically
        /// impossible for an account the firm was policing. And more than a fifth
        /// of the account size, so a genuine blow-through on a small drawdown -
        /// a 50K with a 2,000 drawdown that gapped 5,000 through its floor - is
        /// still reported as what it is, a dead account, rather than excused as a
        /// typo.
        ///
        /// Being ABOVE the configured size is not a mismatch: that is just
        /// profit, and if the size is wrong in that direction the floor comes out
        /// tighter than reality, which reports less room than there is.
        /// </summary>
        public bool ConfigMismatch
        {
            get
            {
                if (!HasValidEquity) return false;
                if (StartingBalance <= 0 || TrailingDrawdown <= 0) return false;

                double shortfall = StartingBalance - CurrentEquity;
                if (shortfall <= 0) return false;

                return shortfall > TrailingDrawdown * 2.0
                    && shortfall > StartingBalance * 0.20;
            }
        }

        /// <summary>
        /// True when the balance is already at or below the floor. Either the
        /// account is finished, or - far more likely on a sim or long-running
        /// account - the configured account size does not match reality. The
        /// second case is now caught by ConfigMismatch and excluded here, so this
        /// means what it says.
        /// </summary>
        public bool PastFloor
        {
            get { return HasValidEquity && CushionToFloor <= 0 && !ConfigMismatch; }
        }
        public bool HasValidEquity = true;
        public bool ExecutionTelemetryHealthy = true;
        public string ExecutionTelemetryWarning = "";
        public DrawdownType DrawdownType = DrawdownType.Intraday;

        /// <summary>Traded by a strategy rather than by hand.</summary>
        public bool IsAutomated;

        /// <summary>The firm's target for passing this account. Display only.</summary>
        public double ProfitTarget;

        /// <summary>What the account started at, so progress toward passing can be shown.</summary>
        public double StartingBalance;

        public bool LastTradeWasLoss;
        public int MinutesSinceLastLoss = -1;   // -1 == no loss yet today
        public int CooldownMinutes = 5;

        /// <summary>
        /// True once today's P&L has been at or past the daily loss limit, even
        /// if it has since come back.
        ///
        /// Hitting a daily loss limit is an EVENT, not a state. It used to be
        /// read as a state - "are you down more than your limit right now?" -
        /// which meant a trader who went past their limit, took one more trade
        /// and won, watched the account go from a hard stop back to a caution.
        /// The rule un-fired. Worse, it un-fired as a direct reward for taking
        /// the trade the rule existed to prevent, and it taught the exact lesson
        /// the whole product argues against: that you can trade your way back out
        /// of a bad day.
        ///
        /// You cannot un-lose the money. The day has already cost you the most
        /// you said you were willing to lose, and that stays true whatever
        /// happens next.
        /// </summary>
        public bool DailyLossLimitHit;

        /// <summary>The worst today has been, so the limit can say what it cost.</summary>
        public double WorstDailyPnl;

        public int NowMinuteEt;                 // minutes since midnight, platform clock
        /// <summary>Trading window. Start == end means none is set, and nothing is said about the clock.</summary>
        public int SessionStartMinute = 0;
        public int SessionEndMinute = 0;
    }

    public class DisciplineDecision
    {
        public DisciplineAction Action;
        public Urgency Urgency;
        public string Reason;
        public string Headline;
        public List<string> Bullets = new List<string>();
        public List<RiskSignal> Signals = new List<RiskSignal>();
    }

    public static class DisciplineEngine
    {
        /// <summary>
        /// The one line this account most needs to say, short enough to sit on a
        /// row. The headline is written for the whole window; this is written for
        /// a single account among several.
        ///
        /// Every account gets its own, because a trader running six accounts was
        /// previously told about one of them and left to guess about the rest -
        /// and the account quietly handing back a green day is exactly the one
        /// that does not shout.
        /// </summary>
        public static string RowWarning(DisciplineInput i, DisciplineDecision d)
        {
            if (i == null || d == null) return "";

            if (!i.HasValidEquity) return "no balance yet";
            if (!i.ExecutionTelemetryHealthy) return "execution feed mismatch - verify in Accounts";
            if (i.ConfigMismatch)
                return "set up as a " + Money(i.StartingBalance) + " account but it holds "
                     + Money(i.CurrentEquity) + " - check its rules";
            if (i.PastFloor) return "at or below its floor";

            if (Has(d.Signals, "daily_loss_limit"))
                return i.DailyPnl <= -Math.Abs(i.DailyLossLimit)
                    ? "past the daily loss limit - done for the day"
                    : "hit the " + Money(i.DailyLossLimit) + " daily limit earlier today - winning "
                      + "some back does not give the day back";

            if (Has(d.Signals, "loss_streak"))
                return i.LossStreak + " losses in a row - this is your stop line";

            // Terminal before temporary here too, or the row's words disagree
            // with its own WHAT TO DO column.
            if (Has(d.Signals, "over_trading"))
                return i.TradesToday > i.MaxTrades
                    ? i.TradesToday + " trades - PAST your limit of " + i.MaxTrades
                    : i.TradesToday + " trades - that is your limit, the day is done";

            if (Has(d.Signals, "give_back"))
                return "was up " + Money(i.PeakDailyPnl) + ", handed back "
                     + Money(i.PeakDailyPnl - i.DailyPnl) + " - do not trade back your profits";

            if (Has(d.Signals, "revenge_window"))
                return "only " + i.MinutesSinceLastLoss + " min since a loss - wait it out";

            if (Has(d.Signals, "thin_cushion"))
                return "only " + Money(i.CushionToFloor) + " left - one stop could end it";

            if (Has(d.Signals, "over_size"))
                return "holding " + i.OpenContracts + " over a cap of " + i.MaxContracts;

            if (d.Action == DisciplineAction.ProtectGreen)
                return "target hit - bank it or free-roll, do not give it back";

            if (Has(d.Signals, "size_throttled"))
                return "size down to " + i.MaxContracts + " while this account is down";

            if (Has(d.Signals, "out_of_window"))
                return "outside your trading window (" + WindowLabel(i.SessionStartMinute, i.SessionEndMinute) + ")";

            if (i.DailyPnl > 0) return "green " + Money(i.DailyPnl) + " - protect it";
            return "clear";
        }

        private static string Money(double n)
        {
            double r = Math.Round(n);
            string s = Math.Abs(r).ToString("N0");
            return (r < 0 ? "-$" : "$") + s;
        }

        /// <summary>
        /// Is this minute inside the trader's trading window?
        ///
        /// Three cases, and the first two were both wrong before this existed.
        ///
        /// A window that was never set up: Ballast shipped with 09:30-11:30
        /// built in and no way to change it, so anyone trading the afternoon -
        /// or the overnight session - was told every single day that they were
        /// outside a window they had never chosen. Start == end now means no
        /// window at all, and nothing is ever said about the clock.
        ///
        /// A window that crosses midnight: 18:00-02:00 is a normal futures
        /// session and used to be read as "start 1080, end 120", which is empty -
        /// so it was outside the window for all twenty-four hours.
        ///
        /// And the ordinary case, start before end.
        /// </summary>
        public static bool InSessionWindow(int nowMinute, int startMinute, int endMinute)
        {
            if (startMinute == endMinute) return true;
            if (startMinute < endMinute) return nowMinute >= startMinute && nowMinute <= endMinute;
            return nowMinute >= startMinute || nowMinute <= endMinute;
        }

        /// <summary>"09:30-11:30", or "any time" when no window is set.</summary>
        public static string WindowLabel(int startMinute, int endMinute)
        {
            if (startMinute == endMinute) return "any time";
            return HourMinute(startMinute) + "-" + HourMinute(endMinute);
        }

        public static string HourMinute(int minuteOfDay)
        {
            int m = minuteOfDay;
            if (m < 0) m = 0;
            m = m % 1440;
            int h = m / 60;
            int mm = m % 60;
            return (h < 10 ? "0" : "") + h.ToString(CultureInfo.InvariantCulture)
                 + ":" + (mm < 10 ? "0" : "") + mm.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse "09:30", "9:30", "930" or "9" into minutes past midnight.
        /// Returns -1 when it cannot be read, so a typo never silently becomes
        /// midnight and locks a trader out of their own afternoon.
        /// </summary>
        public static int ParseHourMinute(string text)
        {
            if (text == null) return -1;
            string s = text.Trim();
            if (s.Length == 0) return -1;

            int h, m;
            int colon = s.IndexOf(':');
            if (colon < 0) colon = s.IndexOf('.');

            if (colon >= 0)
            {
                string hp = s.Substring(0, colon).Trim();
                string mp = s.Substring(colon + 1).Trim();
                if (!int.TryParse(hp, NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return -1;
                if (mp.Length == 0) m = 0;
                else if (!int.TryParse(mp, NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) return -1;
            }
            else
            {
                int raw;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw)) return -1;
                if (raw < 0) return -1;
                if (s.Length <= 2) { h = raw; m = 0; }
                else { h = raw / 100; m = raw % 100; }
            }

            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }

        public static List<RiskSignal> DetectRiskSignals(DisciplineInput i)
        {
            List<RiskSignal> signals = new List<RiskSignal>();

            if (!i.ExecutionTelemetryHealthy)
            {
                string detail = string.IsNullOrEmpty(i.ExecutionTelemetryWarning)
                    ? "The account position does not match Ballast's execution ledger."
                    : i.ExecutionTelemetryWarning;
                signals.Add(new RiskSignal("telemetry_gap", Severity.High,
                    detail + " Ballast will not clear another trade until the feed is reconciled."));
            }

            if (i.LossStreak >= i.MaxLossesBeforeStop && i.MaxLossesBeforeStop > 0)
            {
                signals.Add(new RiskSignal("loss_streak", Severity.High,
                    "You've taken " + i.LossStreak + " losses in a row - your stop-after-" + i.MaxLossesBeforeStop + " line."));
            }

            if (i.DailyLossLimit > 0)
            {
                bool downNow = i.DailyPnl <= -Math.Abs(i.DailyLossLimit);

                if (downNow)
                {
                    signals.Add(new RiskSignal("daily_loss_limit", Severity.High,
                        "Down " + Money(-i.DailyPnl) + " - at or past your "
                        + Money(i.DailyLossLimit) + " daily limit."));
                }
                else if (i.DailyLossLimitHit)
                {
                    // Won some of it back. The limit still stands - see
                    // DailyLossLimitHit for why this is not an oversight.
                    double worst = i.WorstDailyPnl < 0 ? -i.WorstDailyPnl : Math.Abs(i.DailyLossLimit);

                    signals.Add(new RiskSignal("daily_loss_limit", Severity.High,
                        "You hit your " + Money(i.DailyLossLimit) + " daily limit today - down "
                        + Money(worst) + " at the worst of it. You are back to "
                        + Money(i.DailyPnl) + " now, and that does not give the day back: your limit "
                        + "is the most you were willing to lose today, and today has already cost "
                        + "you that. Winning some of it afterwards is the trade that was not "
                        + "supposed to happen, not a reason to take the next one."));
                }
            }

            if (i.LastTradeWasLoss && i.MinutesSinceLastLoss >= 0 && i.MinutesSinceLastLoss < i.CooldownMinutes)
            {
                signals.Add(new RiskSignal("revenge_window", Severity.High,
                    "Only " + i.MinutesSinceLastLoss + " min since a loss - inside the " + i.CooldownMinutes +
                    "-min cooldown. This is where revenge trades live."));
            }

            // Thin cushion matters most on intraday-trailing accounts, where floating
            // gains ratchet the floor up and never give it back.
            double cushionThreshold = Math.Max(i.DailyLossLimit, 400.0);

            // At or below the floor is a STATE, not a small cushion. Showing it as
            // a negative "can lose" figure is meaningless - you cannot lose minus
            // twelve thousand dollars - and it hides the far likelier cause, which
            // is that the configured account size does not match this account.
            if (i.ConfigMismatch)
            {
                // Not a stop. Ballast does not know this account's real floor, and
                // saying STOP on a number it cannot stand behind is how the next
                // stop - a true one - gets waved away.
                signals.Add(new RiskSignal("config_mismatch", Severity.High,
                    "This account is set up as a " + Money(i.StartingBalance) + " account with a "
                  + Money(i.TrailingDrawdown) + " drawdown, but it holds " + Money(i.CurrentEquity)
                  + ". Those cannot both be true - a firm closes an account the moment it touches "
                  + "its floor, so a live account is never this far below one. Until the size in "
                  + "Setup matches the account, every figure worked out from it is wrong, and "
                  + "Ballast is not going to guess at your cushion. Open its rules and set the "
                  + "size it actually is."));
            }
            else if (i.HasValidEquity && i.CushionToFloor <= 0)
            {
                signals.Add(new RiskSignal("past_floor", Severity.High,
                    "Balance " + Money(i.CurrentEquity) + " is at or below the floor of "
                  + Money(i.FloorLevel) + ". If this is a live funded account it is finished. "
                  + "If that looks wrong, the account size in Setup does not match this account - "
                  + "fix it there, because every figure Ballast shows is worked out from it."));
            }
            else if (i.CushionToFloor > 0 && i.CushionToFloor < cushionThreshold)
            {
                signals.Add(new RiskSignal("thin_cushion",
                    i.DrawdownType == DrawdownType.Intraday ? Severity.High : Severity.Medium,
                    "Only " + Money(i.CushionToFloor) + " to your trailing floor. One full stop could end the account."));
            }

            if (i.DailyTarget > 0 && i.PeakDailyPnl >= i.DailyTarget && i.DailyPnl <= i.PeakDailyPnl * 0.6)
            {
                signals.Add(new RiskSignal("give_back", Severity.High,
                    "You were up " + Money(i.PeakDailyPnl) + " and have handed back " +
                    Money(i.PeakDailyPnl - i.DailyPnl) + ". Protect the green."));
            }

            if (i.MaxContracts > 0 && i.OpenContracts > i.MaxContracts)
            {
                string why = i.SizeThrottled
                    ? " cap - throttled down from " + i.BaseMaxContracts + " because of what this "
                      + "account has already lost. Sizing up to win it back is how the blowup starts."
                    : " cap. Sizing up is how the blowup starts.";

                signals.Add(new RiskSignal("over_size", Severity.Medium,
                    "You're holding " + i.OpenContracts + " contracts - over your " + i.MaxContracts + why));
            }

            // Fires before any position is on, so the size is known BEFORE the
            // order goes in rather than being criticised afterwards.
            if (i.SizeThrottled && i.OpenContracts <= i.MaxContracts)
            {
                signals.Add(new RiskSignal("size_throttled", Severity.Low,
                    "Max size is " + i.MaxContracts + " right now, down from " + i.BaseMaxContracts +
                    ". This account has spent enough of its drawdown that full size is no longer safe."));
            }

            if (i.MaxTrades > 0 && i.TradesToday >= i.MaxTrades)
            {
                signals.Add(new RiskSignal("over_trading", Severity.Medium,
                    "That's " + i.TradesToday + " trades on this account - its max is " + i.MaxTrades + ". Other accounts have their own count."));
            }

            if (!InSessionWindow(i.NowMinuteEt, i.SessionStartMinute, i.SessionEndMinute))
            {
                signals.Add(new RiskSignal("out_of_window", Severity.Low,
                    "Outside your trading window (" + WindowLabel(i.SessionStartMinute, i.SessionEndMinute)
                    + "). This is where afternoon revenge trades happen."));
            }

            return signals;
        }

        private static bool Has(List<RiskSignal> signals, string key)
        {
            for (int n = 0; n < signals.Count; n++)
                if (signals[n].Key == key) return true;
            return false;
        }

        private static string Verb(DisciplineAction a)
        {
            switch (a)
            {
                case DisciplineAction.StopForDay:  return "Stop";
                case DisciplineAction.Lockout:     return "Lock out";
                case DisciplineAction.Cooldown:    return "Step away";
                case DisciplineAction.SizeDown:    return "Size down";
                case DisciplineAction.ProtectGreen:return "Protect it";
                case DisciplineAction.Trade:       return "Clear to trade";
                case DisciplineAction.CheckSetup:  return "Check its rules";
                default:                           return "Hold";
            }
        }

        /// <summary>
        /// Evaluate current state and return the single most important next action.
        /// Checked in priority order — the first hard breaker wins, because in a
        /// give-back/revenge profile the job is to stop the worst thing first.
        /// </summary>
        public static DisciplineDecision Evaluate(DisciplineInput i)
        {
            DisciplineDecision d = new DisciplineDecision();
            d.Signals = DetectRiskSignals(i);

            DisciplineAction action = DisciplineAction.Trade;
            Urgency urgency = Urgency.Calm;
            // Reads as "Clear to trade - <reason>", so the reason must not carry
            // its own dash or the headline stutters.
            string reason = "conditions are clean, trade your plan";

            if (Has(d.Signals, "telemetry_gap"))
            {
                action = DisciplineAction.Lockout; urgency = Urgency.Alert;
                reason = "execution telemetry is incomplete - verify the account before trading";
            }
            else if (Has(d.Signals, "config_mismatch"))
            {
                // Deliberately Caution, not Alert. Nothing here says the trader
                // did anything wrong, and a red STOP that is plainly nonsense -
                // thrown at an account that is up on the day - costs more than it
                // saves, because it teaches the trader to read past red.
                action = DisciplineAction.CheckSetup; urgency = Urgency.Caution;
                reason = "this account's settings do not describe this account";
            }
            else if (Has(d.Signals, "past_floor"))
            {
                action = DisciplineAction.Lockout; urgency = Urgency.Alert;
                reason = "this account is at or below its floor";
            }
            else if (Has(d.Signals, "daily_loss_limit"))
            {
                action = DisciplineAction.Lockout; urgency = Urgency.Alert;
                reason = "this account has hit its daily loss limit";
            }
            else if (Has(d.Signals, "loss_streak"))
            {
                action = DisciplineAction.StopForDay; urgency = Urgency.Alert;
                reason = "this account has taken its max losses for today";
            }
            else if (Has(d.Signals, "over_trading"))
            {
                // Above the cooldown and the give-back, and this is the whole
                // point of where it sits.
                //
                // "it says wait on ballast but on the chart it is done for the
                // day, so that is misleading"
                //
                // Both were reading the same account correctly. At 7 trades of 7
                // he was also 4 minutes past a loss, and the cooldown was
                // checked first - so the row said WAIT while the chart said
                // DONE FOR THE DAY. WAIT is a promise that something changes if
                // he waits. Nothing changes: the day is over, and at 11:30 he
                // would have waited out the clock and found the door still shut.
                //
                // A state he can wait out must never outrank a state he cannot.
                // Terminal before temporary, always.
                action = DisciplineAction.StopForDay;
                bool past = i.TradesToday > i.MaxTrades;
                urgency = Urgency.Alert;
                reason = past
                    ? "this account is past its max trades for today"
                    : "this account is at its max trades for today";
            }
            else if (Has(d.Signals, "give_back"))
            {
                action = DisciplineAction.ProtectGreen; urgency = Urgency.Alert;
                reason = "you're handing back a green day";
            }
            else if (Has(d.Signals, "revenge_window"))
            {
                action = DisciplineAction.Cooldown; urgency = Urgency.Alert;
                reason = "you just took a loss - wait out the tilt window";
            }
            else if (Has(d.Signals, "thin_cushion"))
            {
                action = DisciplineAction.SizeDown; urgency = Urgency.Caution;
                reason = "your cushion to the trailing floor is thin";
            }
            else if (Has(d.Signals, "over_size"))
            {
                action = DisciplineAction.SizeDown; urgency = Urgency.Caution;
                reason = "you're over your contract cap";
            }
            else if (Has(d.Signals, "out_of_window"))
            {
                action = DisciplineAction.None; urgency = Urgency.Caution;
                reason = "you're outside your trading window";
            }
            else if (Has(d.Signals, "size_throttled"))
            {
                // Lowest priority on purpose. It is not a warning that something
                // has gone wrong - it is a smaller size being handed to a trader
                // who is otherwise clear to trade.
                action = DisciplineAction.SizeDown; urgency = Urgency.Calm;
                reason = "clear to trade, but at " + i.MaxContracts
                       + (i.MaxContracts == 1 ? " contract" : " contracts") + " while this account is down";
            }

            // Hit the target cleanly with no warnings — celebrate the discipline.
            if (action == DisciplineAction.Trade && i.DailyTarget > 0 && i.DailyPnl >= i.DailyTarget)
            {
                action = DisciplineAction.ProtectGreen; urgency = Urgency.Caution;
                reason = "you've hit your daily target - bank it or free-roll";
            }

            d.Action = action;
            d.Urgency = urgency;
            d.Reason = reason;
            d.Headline = Verb(action) + " - " + reason;

            // Worst first, so the most important line reads first.
            d.Signals.Sort(delegate(RiskSignal a, RiskSignal b) { return ((int)b.Severity).CompareTo((int)a.Severity); });
            for (int n = 0; n < d.Signals.Count; n++) d.Bullets.Add(d.Signals[n].Summary);

            return d;
        }

        /// <summary>
        /// The actual dollar level the account dies at.
        ///
        /// The floor starts at (startingBalance - trailingDrawdown) and ratchets up
        /// behind new highs, never down. On intraday-trailing accounts it follows the
        /// PEAK — including unrealised profit — so a winner that round-trips still
        /// moves the floor up permanently.
        ///
        /// lockFloorAt: most firms STOP the trailing once the floor would reach a set
        /// level (Apex ~ starting + $100, Topstep ~ the original starting balance).
        /// Past that point there is no trailing drawdown at all, just a fixed floor.
        /// Pass 0 for accounts that trail forever.
        /// </summary>
        public static double FloorLevel(double startingBalance, double trailingDrawdown,
                                        double currentBalance, double peakBalance,
                                        DrawdownType type, double lockFloorAt)
        {
            // For EOD accounts peakBalance is the persisted completed-session
            // high-water anchor. The current intraday balance must not move the
            // floor down after a losing trade or up before the session closes.
            double anchor = (type == DrawdownType.Intraday)
                ? Math.Max(peakBalance, currentBalance)
                : peakBalance;

            // A missing/migrated anchor is never allowed to lower the initial
            // floor. Callers should surface missing state separately; this is the
            // final arithmetic guardrail.
            if (anchor < startingBalance) anchor = startingBalance;

            double trailed = anchor - trailingDrawdown;

            // Never below where it started.
            double floor = Math.Max(trailed, startingBalance - trailingDrawdown);

            // Once it reaches the lock level it stops trailing for good.
            if (lockFloorAt > 0 && floor >= lockFloorAt) floor = lockFloorAt;

            return floor;
        }

        /// <summary>Dollars of room between the current balance and the floor.</summary>
        public static double CushionToFloor(double startingBalance, double trailingDrawdown,
                                            double currentBalance, double peakBalance,
                                            DrawdownType type, double lockFloorAt)
        {
            double floor = FloorLevel(startingBalance, trailingDrawdown, currentBalance,
                                      peakBalance, type, lockFloorAt);
            return currentBalance - floor;
        }

        /// <summary>True once the floor has locked and no longer trails.</summary>
        public static bool FloorIsLocked(double startingBalance, double trailingDrawdown,
                                         double currentBalance, double peakBalance,
                                         DrawdownType type, double lockFloorAt)
        {
            if (lockFloorAt <= 0) return false;
            double anchor = (type == DrawdownType.Intraday)
                ? Math.Max(peakBalance, currentBalance)
                : peakBalance;
            if (anchor < startingBalance) anchor = startingBalance;
            return (anchor - trailingDrawdown) >= lockFloorAt;
        }
    }
}
