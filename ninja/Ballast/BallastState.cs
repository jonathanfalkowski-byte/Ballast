// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastState.cs
//
// A tiny shared noticeboard between the Ballast window and the chart indicator.
//
// The window watches accounts. The indicator draws on charts. They are separate
// NinjaScript worlds - an AddOn cannot paint on a chart, and an Indicator cannot
// see the Ballast window - but NinjaTrader compiles everything into one
// assembly, so a static class is a legitimate and very cheap bridge.
//
// Deliberately dumb: strings in, strings out, no logic. The engine decides what
// to say; this only carries it. If the window is closed the board simply goes
// stale, and the indicator says so rather than showing a warning from an hour
// ago as though it were current.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ballast
{
    public class AccountState
    {
        public string Warning = "";
        /// <summary>0 calm, 1 caution, 2 alert. Drives the colour on the chart.</summary>
        public int Urgency;
        public string Headline = "";
        public double CanLose;
        public bool HasCushion;
        public DateTime UpdatedAt = DateTime.MinValue;

        /// <summary>
        /// True while this account has tripped a hard breaker - past its floor,
        /// past its daily loss limit, or past its max losses for the day.
        ///
        /// Deliberately independent of whether the trader has typed past the
        /// Ballast window. Overriding buys quiet in the app; it does not buy a
        /// clean-looking chart, and the chart is where they are actually looking.
        /// </summary>
        public bool Locked;
        public string LockLine = "";

        // ── The running count ────────────────────────────────────────────────
        //
        // The chart used to show either a warning or the words "BALLAST OK", and
        // nothing else. That meant a trader who changed an account's rules saw no
        // difference on the chart at all, and reasonably concluded the indicator
        // was not picking the change up. It was; there was simply nothing on the
        // chart that could ever show it.
        //
        // These are the three numbers a trader actually tracks in their head all
        // session - trades taken, losses in a row, and how much of today's budget
        // is left - so they are published whether or not anything is wrong.
        public int TradesToday;
        public int MaxTrades;
        public int LossesToday;
        public int MaxLosses;
        /// <summary>Dollars left before today's loss limit is hit. 0 when no limit is set.</summary>
        public double RoomToday;
        /// <summary>Today's target and whether one is set - the other end of the same decision as RoomToday.</summary>
        public double DailyTarget;
        public bool HasDailyLimit;
        public double DailyPnl;
    }

    public static class BallastState
    {
        private static readonly Dictionary<string, AccountState> states =
            new Dictionary<string, AccountState>(StringComparer.OrdinalIgnoreCase);

        private static readonly object gate = new object();

        /// <summary>
        /// How long a posted warning stays trustworthy. A chart quietly showing a
        /// stale "you are fine" after the window closed would be worse than a
        /// chart showing nothing at all.
        /// </summary>
        public static int StaleAfterSeconds = 15;

        public static void Publish(string account, string warning, int urgency,
                                   string headline, double canLose, bool hasCushion, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;

            lock (gate)
            {
                AccountState s;
                if (!states.TryGetValue(account, out s)) { s = new AccountState(); states[account] = s; }

                s.Warning = warning ?? "";
                s.Urgency = urgency;
                s.Headline = headline ?? "";
                s.CanLose = canLose;
                s.HasCushion = hasCushion;
                s.UpdatedAt = now;
            }
        }

        /// <summary>
        /// The running count for one account. Published every tick alongside the
        /// warning, so a chart can show where the trader stands even on a calm
        /// day - which is most days, and is exactly when the numbers are worth
        /// glancing at rather than reacting to.
        /// </summary>
        public static void PublishCount(string account, int tradesToday, int maxTrades,
                                        int lossesToday, int maxLosses,
                                        double roomToday, bool hasDailyLimit,
                                        double dailyPnl, double dailyTarget, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;

            lock (gate)
            {
                AccountState s;
                if (!states.TryGetValue(account, out s)) { s = new AccountState(); states[account] = s; }

                s.TradesToday = tradesToday;
                s.MaxTrades = maxTrades;
                s.LossesToday = lossesToday;
                s.MaxLosses = maxLosses;
                s.RoomToday = roomToday;
                s.HasDailyLimit = hasDailyLimit;
                s.DailyPnl = dailyPnl;
                s.DailyTarget = dailyTarget;
                if (s.UpdatedAt == DateTime.MinValue) s.UpdatedAt = now;
            }
        }

        /// <summary>
        /// The quiet one-line status for a calm chart: what you have done today
        /// and what is left. Short enough to ignore, specific enough to be worth
        /// a glance, and it changes when the account's rules change - which is
        /// what makes the indicator visibly alive.
        /// </summary>
        public static string ChartCount(AccountState s, string account)
        {
            if (s == null) return "";

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(account)) sb.Append(account.ToUpperInvariant()).Append("   ");

            // A bare count agrees with itself - "1 TRADE". A ratio does not:
            // "1/5 TRADE" is wrong, because the noun belongs to the limit, not to
            // the count. Small thing, but a chart banner that reads as broken
            // English undermines every number next to it.
            sb.Append(s.TradesToday);
            if (s.MaxTrades > 0) sb.Append('/').Append(s.MaxTrades);
            sb.Append(s.MaxTrades <= 0 && s.TradesToday == 1 ? " TRADE" : " TRADES");

            sb.Append("   ").Append(s.LossesToday);
            if (s.MaxLosses > 0) sb.Append('/').Append(s.MaxLosses);
            sb.Append(s.MaxLosses <= 0 && s.LossesToday == 1 ? " LOSS" : " LOSSES");

            // Shorter than the window's wording on purpose. This line competes
            // for space with a price chart and gets read sideways, in a second,
            // while a position is on - so every word that is not carrying a
            // number comes out.

            // What is left of today's budget, and what would let you stop. These
            // belong together: one is the most this day can cost, the other is
            // the number that makes walking away a decision rather than a
            // sacrifice. The budget never grows on a green day - see RoomToday
            // in the window - so a good morning does not quietly buy a bigger
            // afternoon to lose.
            if (s.HasDailyLimit)
                sb.Append("   ").Append(Money(s.RoomToday)).Append(" LEFT");

            if (s.DailyTarget > 0)
            {
                if (s.DailyPnl >= s.DailyTarget) sb.Append("   TARGET HIT");
                else sb.Append("   ").Append(Money(s.DailyPnl > 0 ? s.DailyPnl : 0))
                       .Append('/').Append(Money(s.DailyTarget)).Append(" TARGET");
            }

            if (s.HasCushion)
                sb.Append("   ").Append(Money(s.CanLose)).Append(" TO FLOOR");

            return sb.ToString();
        }

        private static string Money(double v)
        {
            double r = Math.Round(v);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Set the hard-breaker flag without disturbing the rest of the state.
        /// Published separately because the window works out the row warning and
        /// the lockout at different points in its tick, and one must not silently
        /// blank the other.
        /// </summary>
        public static void PublishLock(string account, bool locked, string line, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;

            lock (gate)
            {
                AccountState s;
                if (!states.TryGetValue(account, out s)) { s = new AccountState(); states[account] = s; }

                s.Locked = locked;
                s.LockLine = locked ? (line ?? "") : "";
                if (s.UpdatedAt == DateTime.MinValue) s.UpdatedAt = now;
            }
        }

        public static void Clear(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            lock (gate) { states.Remove(account); }
        }

        /// <summary>Current state, or null when absent or stale.</summary>
        public static AccountState Get(string account, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return null;

            lock (gate)
            {
                AccountState s;
                if (!states.TryGetValue(account, out s)) return null;
                if ((now - s.UpdatedAt).TotalSeconds > StaleAfterSeconds) return null;
                return s;
            }
        }

        public static List<string> KnownAccounts()
        {
            lock (gate) { return new List<string>(states.Keys); }
        }

        /// <summary>
        /// The line to paint on a chart, or "" for nothing. Only genuinely
        /// actionable states get drawn: covering a trader's chart with "clear"
        /// every second would train them to ignore the banner, and then it is
        /// worthless on the day it says something that matters.
        /// </summary>
        public static string ChartBanner(AccountState s)
        {
            if (s == null) return "";

            // A hard breaker outranks whatever else the row had to say, and it
            // gets said in the plainest possible words. This is the one that has
            // to land from across a desk with a position on.
            // Read once. The window publishes on its own thread, and reading
            // LockLine twice can catch it mid-clear and paint a bare "STOP - ".
            bool locked = s.Locked;
            string line = s.LockLine;

            if (locked)
            {
                if (string.IsNullOrEmpty(line)) line = "You are done for the day.";
                return ("STOP - " + line).ToUpperInvariant();
            }

            if (s.Urgency <= 0) return "";
            if (s.Warning.Length == 0) return "";
            return s.Warning.ToUpperInvariant();
        }

        /// <summary>True when the banner should be painted in the alarm colour.</summary>
        public static bool IsAlarm(AccountState s)
        {
            return s != null && (s.Locked || s.Urgency >= 2);
        }
    }
}
