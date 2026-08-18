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
using System.Globalization;

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
        /// The rule book, so payout terms can be looked up per account.
        /// Optional - a monitor with no rule book shows no consistency ceiling,
        /// which is the same answer as a firm that publishes none.
        /// </summary>
        public RuleBook Rules;

        // The per-day grouping is the only expensive part of the consistency
        // arithmetic and it changes only when the journal does, so it is worked
        // out once per journal change per account rather than on every tick.
        private readonly Dictionary<string, List<PayoutDay>> payoutDays =
            new Dictionary<string, List<PayoutDay>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> payoutDaysKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                // Market Replay hands the account its money back the moment the
                // clock moves to another day. That is not a surprise worth
                // asking about, and left unanswered it would carry yesterday's
                // peak - and so yesterday's floor - into a fresh account.
                t.AutoResets = RuleBook.IsPracticeAccountName(accountName);

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
            n.TradingDayResetMinute = c.TradingDayResetMinute;
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
            File(e, accountName, now);
            return e;
        }

        /// <summary>Route an immutable fill to the account's per-instrument ledger.</summary>
        public BallastTrade OnExecution(string accountName, string executionId, string instrument,
                                        int signedQuantity, double price, double pointValue,
                                        double commission, DateTime now)
        {
            BallastTracker t = Get(accountName);
            if (t == null) return null;

            BallastTrade e = t.OnExecution(executionId, instrument, signedQuantity, price,
                                           pointValue, commission, now, accountName);
            File(e, accountName, now);
            return e;
        }

        /// <summary>
        /// Everything a trader practises on, kept away from everything he traded.
        /// </summary>
        public readonly PracticeBook Practice = new PracticeBook();

        /// <summary>
        /// Put a finished round trip where it belongs - and the single most
        /// important thing here is that replay never reaches the journal.
        ///
        /// On a Playback connection "now" is the REPLAY clock, so a trade taken
        /// while replaying the sixth of August arrives here stamped the sixth of
        /// August. One Journal.Add and it is sitting beside the funded trades
        /// taken that morning, indistinguishable, feeding the setup edges and
        /// the pressure profile and every answer he relies on. Rewind and run
        /// the morning again and the same trade is filed a second time as a
        /// second piece of evidence.
        ///
        /// This is the only door into the journal, which is why the check lives
        /// here rather than at the two call sites above.
        /// </summary>
        private void File(BallastTrade e, string accountName, DateTime now)
        {
            if (e == null) return;

            if (RuleBook.IsPracticeAccountName(accountName))
            {
                // DateTime.Now, deliberately, and it is the one place in Ballast
                // that wants it: "now" is the replayed clock, and what orders one
                // attempt against another is when he actually sat down to make
                // it.
                PracticeRun run = Practice.RunFor(accountName, now, DateTime.Now);
                run.Trades.Add(e);
                return;
            }

            Journal.Add(e);
        }

        /// <summary>Evaluate one account and return its snapshot.</summary>
        public AccountSnapshot Evaluate(string accountName, DateTime now)
        {
            BallastTracker t = Get(accountName);
            if (t == null) return null;

            AccountSnapshot s = new AccountSnapshot();
            s.AccountName = accountName;
            s.Input = t.BuildInput(now);
            ApplyPayoutStanding(accountName, t, s.Input, now);
            s.Decision = DisciplineEngine.Evaluate(s.Input);
            return s;
        }

        /// <summary>
        /// Put the consistency ceiling on the input, when there is one.
        ///
        /// Deliberately silent in four cases, and in each of them a number
        /// would be worse than no number:
        ///
        ///   - a practice or replay account. There is no payout to protect.
        ///   - an EVALUATION. "just so you know none of those accounts are PA
        ///     all are evals" - and an evaluation cannot be withdrawn from, so
        ///     it has no consistency rule to break. The rule book already
        ///     reaches this answer on its own, because an evaluation plan has
        ///     no PAYOUT line; this is the second lock, for the day an account
        ///     is set up as the wrong type. That has happened here before, and
        ///     a ceiling on an account that cannot pay out would be advice to
        ///     stop trading for no reason at all.
        ///   - a firm with no PAYOUT line in the rule book. Borrowing another
        ///     firm's percentage would tell him to stop on a day his own firm
        ///     was perfectly happy with.
        ///   - an account past the end of its payout ladder, where the firm
        ///     itself stops applying the rule.
        ///   - a day with nothing banked underneath it, where the arithmetic
        ///     says "stop at zero" and stopping achieves nothing. What he needs
        ///     there is more days, not a smaller day.
        /// </summary>
        private void ApplyPayoutStanding(string accountName, BallastTracker t,
                                         DisciplineInput i, DateTime now)
        {
            if (i == null || t == null || Rules == null) return;
            if (RuleBook.IsPracticeAccountName(accountName)) return;
            if (t.Config != null && (t.Config.Purpose == AccountPurpose.Practice
                                  || t.Config.Purpose == AccountPurpose.Evaluation)) return;

            PayoutRules pr = Rules.PayoutForAccount(accountName, t.Config);
            if (pr == null || !pr.HasConsistencyRule) return;

            if (pr.MaxPayouts > 0 && t.Config != null && t.Config.PayoutsTaken >= pr.MaxPayouts)
                return;

            DateTime since = t.Config != null ? t.Config.LastPayoutOn : DateTime.MinValue.Date;
            int reset = t.Config != null ? t.Config.TradingDayResetMinute : 0;

            string key = Journal.Count.ToString(CultureInfo.InvariantCulture) + "|"
                       + since.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|"
                       + reset.ToString(CultureInfo.InvariantCulture);

            List<PayoutDay> days;
            string had;
            if (!payoutDaysKey.TryGetValue(accountName, out had) || had != key
                || !payoutDays.TryGetValue(accountName, out days) || days == null)
            {
                days = PayoutBook.Days(Journal.All, accountName, since, reset);
                payoutDays[accountName] = days;
                payoutDaysKey[accountName] = key;
            }

            PayoutStanding st = PayoutBook.Stand(days, t.TradingDay(now), i.DailyPnl, pr);
            if (!st.Known || !st.CeilingWorthShowing) return;

            i.ConsistencyPct = pr.ConsistencyPct;
            i.WindfallCeiling = st.CeilingToday;
            i.PastWindfallCeiling = st.PastCeiling;
            i.ProfitToUnblockPayout = st.ProfitToUnblock;
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
                case DisciplineAction.CheckSetup:   action = 1; break;
                case DisciplineAction.None:         action = 1; break;
                default:                            action = 0; break; // Trade
            }

            return urgency * 10 + action;
        }

        /// <summary>
        /// The states that mean the ACCOUNT is in trouble, as opposed to the
        /// trader. A bot cannot be told to wait out a cooldown, but an account
        /// it has run into its floor still needs saying out loud.
        /// </summary>
        private static bool IsTerminal(DisciplineAction a)
        {
            return a == DisciplineAction.Lockout       // past the floor, or the daily loss limit
                || a == DisciplineAction.CheckSetup;   // the figures do not describe this account
        }

        /// <summary>
        /// Enough to drop a bot below every hand-traded account without
        /// disturbing the order among bots themselves. The scale above tops out
        /// at 36.
        /// </summary>
        private const int BotDemotion = 100;

        /// <summary>
        /// Same ladder, but knowing whether a person is at the keyboard.
        ///
        /// "oh it is a bot"
        ///
        /// Severity(d) cannot tell - it only ever saw the decision - so the
        /// headline was picked with no idea which accounts he was actually
        /// trading. Sim110 runs a bot at three trades and three losses, which a
        /// bot reaches in minutes, and StopForDay at Alert scores 35. That
        /// outranks a hand-traded account sitting clear on 10, and a
        /// hand-traded account in a cooldown on 33 - so the loudest line in
        /// Ballast would have spent the day telling him to stop trading an
        /// account he was not trading, over the top of the two he was.
        ///
        /// Discipline states on a bot rank below everything a person is doing.
        /// Terminal ones do not: an account that has run into its floor is not
        /// advice, it is news, and it is still his money whoever placed the
        /// order.
        /// </summary>
        public static int Severity(DisciplineDecision d, DisciplineInput i)
        {
            int score = Severity(d);
            if (d != null && i != null && i.IsAutomated && !IsTerminal(d.Action))
                score -= BotDemotion;
            return score;
        }

        /// <summary>The account you most need to hear about right now.</summary>
        public AccountSnapshot MostUrgent(List<AccountSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return null;

            AccountSnapshot worst = snapshots[0];
            int worstScore = Severity(worst.Decision, worst.Input);

            for (int i = 1; i < snapshots.Count; i++)
            {
                int score = Severity(snapshots[i].Decision, snapshots[i].Input);
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

                // An account whose settings do not describe it has no knowable
                // cushion. Its figure is a huge negative that would take over the
                // headline card and hide the account that really is closest.
                if (snapshots[i].Input.ConfigMismatch) continue;

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
