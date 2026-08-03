// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastMonitor.cs
//
// Multi-account support. Prop traders commonly run several accounts at once
// (copy-traded evaluations, mixed sizes), and each carries its OWN trailing
// drawdown and therefore its own cushion. So every monitored account gets its
// own tracker and its own config.
//
// The trader, however, is one person with one psychology. So the window shows a
// single headline action, driven by whichever account is in the most trouble —
// the same "stop the worst thing first" principle the engine uses internally.
// If any account is about to breach, you should stop trading. All of them.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Ballast
{
    public class AccountSnapshot
    {
        public string AccountName;
        public DisciplineInput Input;
        public DisciplineDecision Decision;
    }

    /// <summary>
    /// Sorts account names the way a person expects. Plain alphabetical ordering
    /// puts APEX-10 before APEX-2, because it compares "1" against "2" character
    /// by character. Prop accounts are almost always numbered, so digit runs are
    /// compared as numbers and everything else case-insensitively.
    /// </summary>
    public class NaturalNameComparer : IComparer<string>
    {
        public static readonly NaturalNameComparer Instance = new NaturalNameComparer();

        public int Compare(string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;

                    // Compare digit runs by value, ignoring leading zeros, so
                    // "007" and "7" tie and fall through to the rest of the name.
                    string da = a.Substring(si, i - si).TrimStart('0');
                    string db = b.Substring(sj, j - sj).TrimStart('0');

                    if (da.Length != db.Length) return da.Length < db.Length ? -1 : 1;
                    int num = string.CompareOrdinal(da, db);
                    if (num != 0) return num < 0 ? -1 : 1;
                }
                else
                {
                    char ca = char.ToUpperInvariant(a[i]);
                    char cb = char.ToUpperInvariant(b[j]);
                    if (ca != cb) return ca < cb ? -1 : 1;
                    i++; j++;
                }
            }

            int leftA = a.Length - i, leftB = b.Length - j;
            if (leftA != leftB) return leftA < leftB ? -1 : 1;
            return 0;
        }
    }

    public class BallastMonitor
    {
        private readonly Dictionary<string, BallastTracker> trackers =
            new Dictionary<string, BallastTracker>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rules for accounts that are NOT currently being watched.
        ///
        /// Un-ticking an account used to throw its settings away, and because the
        /// settings file is written from the watched list, the next save deleted
        /// them from disk as well. A trader with twenty accounts who ticks one off
        /// for an afternoon then loses its drawdown, its daily stop and its size
        /// cap for good - and has no way of knowing that is what un-ticking meant.
        ///
        /// Un-ticking now means "stop watching this", never "forget this". The
        /// only thing that erases an account's rules is the trader changing them.
        /// </summary>
        private readonly Dictionary<string, TrackerConfig> remembered =
            new Dictionary<string, TrackerConfig>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Settings applied to any newly monitored account.</summary>
        public TrackerConfig DefaultConfig = new TrackerConfig();

        /// <summary>
        /// One journal across every account. Accounts are a prop-firm accident;
        /// the trader is one person, and the patterns worth seeing (revenge
        /// entries, unplanned trades) show up across all of them at once.
        /// </summary>
        public BallastJournal Journal = new BallastJournal();

        /// <summary>
        /// Monitored account names, always in the same order the trader sees them
        /// everywhere else. A dictionary hands them back in hash order, which made
        /// the dropdown shuffle itself as accounts were ticked on and off.
        /// </summary>
        public List<string> MonitoredNames
        {
            get
            {
                List<string> names = new List<string>(trackers.Keys);
                names.Sort(NaturalNameComparer.Instance);
                return names;
            }
        }
        public int Count { get { return trackers.Count; } }

        public bool IsMonitored(string accountName)
        {
            return !string.IsNullOrEmpty(accountName) && trackers.ContainsKey(accountName);
        }

        public BallastTracker GetOrCreate(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return null;

            BallastTracker t;
            if (!trackers.TryGetValue(accountName, out t))
            {
                t = new BallastTracker();

                // Its own rules if it has ever had any, the defaults only if it
                // has genuinely never been set up. Re-ticking an account must
                // hand back exactly what un-ticking took away.
                TrackerConfig kept;
                t.Config = remembered.TryGetValue(accountName, out kept)
                    ? CloneConfig(kept)
                    : CloneConfig(DefaultConfig);

                trackers[accountName] = t;
                remembered.Remove(accountName);
            }
            return t;
        }

        public BallastTracker Get(string accountName)
        {
            BallastTracker t;
            if (accountName != null && trackers.TryGetValue(accountName, out t)) return t;
            return null;
        }

        /// <summary>Stop watching an account, keeping its rules for when it comes back.</summary>
        public void Remove(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return;

            BallastTracker t;
            if (trackers.TryGetValue(accountName, out t) && t.Config != null)
                remembered[accountName] = CloneConfig(t.Config);

            trackers.Remove(accountName);
        }

        /// <summary>Rules held for an account that is not being watched, or null.</summary>
        public TrackerConfig RememberedConfig(string accountName)
        {
            TrackerConfig c;
            if (accountName != null && remembered.TryGetValue(accountName, out c)) return c;
            return null;
        }

        /// <summary>True when this account has rules, whether or not it is being watched.</summary>
        public bool HasConfig(string accountName)
        {
            return IsMonitored(accountName)
                || (!string.IsNullOrEmpty(accountName) && remembered.ContainsKey(accountName));
        }

        /// <summary>
        /// Put rules on record without starting to watch the account. Used when
        /// loading the settings file, so an account saved while un-ticked comes
        /// back with everything it had.
        /// </summary>
        public void RememberConfig(string accountName, TrackerConfig c)
        {
            if (string.IsNullOrEmpty(accountName) || c == null) return;
            if (trackers.ContainsKey(accountName)) return;
            remembered[accountName] = CloneConfig(c);
        }

        /// <summary>Un-watched accounts that still have rules on file, in display order.</summary>
        public List<string> RememberedNames
        {
            get
            {
                List<string> names = new List<string>(remembered.Keys);
                names.Sort(NaturalNameComparer.Instance);
                return names;
            }
        }

        /// <summary>Forget an account's rules outright. Only ever called deliberately.</summary>
        public void Forget(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return;
            trackers.Remove(accountName);
            remembered.Remove(accountName);
        }

        /// <summary>Push the current default settings onto every monitored account.</summary>
        public void ApplyDefaultsToAll()
        {
            List<string> names = new List<string>(trackers.Keys);
            for (int i = 0; i < names.Count; i++)
                trackers[names[i]].Config = CloneConfig(DefaultConfig);
        }

        public static TrackerConfig CloneConfig(TrackerConfig c)
        {
            TrackerConfig n = new TrackerConfig();
            n.StartingBalance = c.StartingBalance;
            n.TrailingDrawdown = c.TrailingDrawdown;
            n.DrawdownType = c.DrawdownType;
            n.MaxLossesBeforeStop = c.MaxLossesBeforeStop;
            n.DailyLossLimit = c.DailyLossLimit;
            n.DailyTarget = c.DailyTarget;
            n.ProfitTarget = c.ProfitTarget;
            n.MaxTrades = c.MaxTrades;
            n.MaxContracts = c.MaxContracts;
            n.CooldownMinutes = c.CooldownMinutes;
            n.LockFloorAt = c.LockFloorAt;
            n.ProfileKey = c.ProfileKey;
            n.RiskPctOfDrawdown = c.RiskPctOfDrawdown;
            n.ThrottleStepPct = c.ThrottleStepPct;
            n.ThrottleCutPct = c.ThrottleCutPct;
            n.BaseMaxContracts = c.BaseMaxContracts;
            n.IsAutomated = c.IsAutomated;
            n.FirmMaxContracts = c.FirmMaxContracts;
            n.FirmDailyLossLimit = c.FirmDailyLossLimit;
            n.TrustAccountRealised = c.TrustAccountRealised;
            n.Generation = c.Generation;
            n.SessionStartMinute = c.SessionStartMinute;
            n.SessionEndMinute = c.SessionEndMinute;
            return n;
        }

        /// <summary>
        /// Route a position update to the right account and journal any round-trip
        /// it completed. Returns the new entry, or null if nothing closed.
        /// </summary>
        public BallastTrade OnPosition(string accountName, int signedQuantity, double realisedNow,
                                       DateTime now, string instrument)
        {
            BallastTracker t = Get(accountName);
            if (t == null) return null;

            BallastTrade e = t.OnPosition(signedQuantity, realisedNow, now, instrument, accountName);
            if (e != null) Journal.Add(e);
            return e;
        }

        /// <summary>Evaluate one account and return its snapshot.</summary>
        public AccountSnapshot Evaluate(string accountName, DateTime now)
        {
            BallastTracker t = Get(accountName);
            if (t == null) return null;

            AccountSnapshot s = new AccountSnapshot();
            s.AccountName = accountName;
            s.Input = t.BuildInput(now);
            s.Decision = DisciplineEngine.Evaluate(s.Input);
            return s;
        }

        public List<AccountSnapshot> EvaluateAll(DateTime now)
        {
            List<AccountSnapshot> list = new List<AccountSnapshot>();
            List<string> names = MonitoredNames;

            for (int i = 0; i < names.Count; i++)
            {
                AccountSnapshot s = Evaluate(names[i], now);
                if (s != null) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Higher = more serious. Used to pick the headline account.
        /// Mirrors the engine's own priority ladder.
        /// </summary>
        public static int Severity(DisciplineDecision d)
        {
            if (d == null) return -1;

            int urgency;
            if (d.Urgency == Urgency.Alert) urgency = 3;
            else if (d.Urgency == Urgency.Caution) urgency = 2;
            else urgency = 1;

            int action;
            switch (d.Action)
            {
                case DisciplineAction.Lockout:      action = 6; break;
                case DisciplineAction.StopForDay:   action = 5; break;
                case DisciplineAction.ProtectGreen: action = 4; break;
                case DisciplineAction.Cooldown:     action = 3; break;
                case DisciplineAction.SizeDown:     action = 2; break;
                case DisciplineAction.None:         action = 1; break;
                default:                            action = 0; break; // Trade
            }

            return urgency * 10 + action;
        }

        /// <summary>The account you most need to hear about right now.</summary>
        public AccountSnapshot MostUrgent(List<AccountSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return null;

            AccountSnapshot worst = snapshots[0];
            int worstScore = Severity(worst.Decision);

            for (int i = 1; i < snapshots.Count; i++)
            {
                int score = Severity(snapshots[i].Decision);
                if (score > worstScore)
                {
                    worst = snapshots[i];
                    worstScore = score;
                }
            }
            return worst;
        }

        /// <summary>Combined realised P&L across monitored accounts.</summary>
        public double TotalDailyPnl(List<AccountSnapshot> snapshots)
        {
            double sum = 0;
            if (snapshots == null) return 0;
            for (int i = 0; i < snapshots.Count; i++) sum += snapshots[i].Input.DailyPnl;
            return sum;
        }

        /// <summary>Thinnest cushion across monitored accounts — the binding constraint.</summary>
        public double MinCushion(List<AccountSnapshot> snapshots)
        {
            // Only accounts we actually have equity for. An unknown cushion must
            // not masquerade as the binding constraint.
            double min = double.MaxValue;
            if (snapshots == null) return min;

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!snapshots[i].Input.HasValidEquity) continue;
                if (snapshots[i].Input.CushionToFloor < min) min = snapshots[i].Input.CushionToFloor;
            }
            return min;
        }

        /// <summary>True when at least one monitored account has a usable equity reading.</summary>
        public bool AnyValidEquity(List<AccountSnapshot> snapshots)
        {
            if (snapshots == null) return false;
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i].Input.HasValidEquity) return true;
            return false;
        }
    }
}
