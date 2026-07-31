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
