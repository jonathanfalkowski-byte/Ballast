// ─────────────────────────────────────────────────────────────────────────────
// Ballast — TiltLockout.cs
//
// The thing that gets in the way when a trader has stopped trading and started
// trying to get even.
//
// Design notes, because this file is the one most likely to be softened later by
// someone who finds it annoying — it is SUPPOSED to be annoying, but only in one
// specific way:
//
//   1. It never touches an order. Ballast does not place, modify, cancel or
//      flatten anything, ever. This puts a wall in front of the SCREEN, not in
//      front of the market. A trader who wants to trade can always trade.
//
//   2. The right choice is one click. "I'm done for the day" is the big button.
//      Overriding is the small ugly one, and it costs about ten seconds of
//      typing. That asymmetry is the whole mechanism: long enough to break an
//      automatic loop, short enough that nobody ever feels trapped by software.
//
//   3. Every override is written down and costed against what the session did
//      afterwards. That is what turns overriding from a free action into
//      evidence. The trader is not lectured; they are shown their own record.
//
//   4. The record is reported honestly in both directions. Sessions that
//      recovered after an override are counted too. If this only ever reported
//      losses it would be propaganda, a trader would catch it inside a week, and
//      then nothing else in Ballast would be believed either.
//
// No NinjaTrader and no WPF dependencies live in this file, on purpose: all of
// it is unit-testable on its own.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ballast
{
    /// <summary>The signal keys that are allowed to put a wall on the screen.</summary>
    public static class TiltKind
    {
        public const string PastFloor      = "past_floor";
        public const string DailyLossLimit = "daily_loss_limit";
        public const string LossStreak     = "loss_streak";

        /// <summary>
        /// The trade count. Keyed to the discipline signal it comes from, so the
        /// wall and the row can never disagree about whether it is in force.
        /// </summary>
        public const string MaxTrades      = "over_trading";
        public const string GiveBack       = "give_back";

        /// <summary>
        /// The consistency ceiling. The only wall in here raised by a day going
        /// WELL, and the only one where carrying on costs a delay rather than
        /// money - so it is deliberately not a hard breaker.
        /// </summary>
        public const string Windfall       = "windfall";

        public static string Label(string kind)
        {
            if (kind == PastFloor)      return "past the floor";
            if (kind == DailyLossLimit) return "past the daily loss limit";
            if (kind == LossStreak)     return "max losses for the day";
            if (kind == MaxTrades)      return "max trades for the day";
            if (kind == GiveBack)       return "handing back a green day";
            if (kind == Windfall)       return "big enough to hold up a payout";
            return kind;
        }
    }

    /// <summary>What fired, in the trader's own numbers.</summary>
    public class TiltTrigger
    {
        public string AccountName = "";
        public string Kind = "";

        /// <summary>Big line. Plain, short, no jargon.</summary>
        public string Title = "";
        /// <summary>The evidence. Always specific dollars or counts.</summary>
        public string Line = "";
        /// <summary>The slap. One sentence, true, not preachy.</summary>
        public string Ask = "";

        public double DailyPnl;

        /// <summary>
        /// True only for past_floor, where by far the likeliest cause is a
        /// mis-set account size rather than a dead account. When this is set the
        /// overlay must offer a way out that does NOT make the trader confess to
        /// tilt — being accused of revenge trading by a configuration bug is the
        /// fastest way to get a feature like this switched off for good.
        /// </summary>
        public bool ConfigSuspect;

        public bool Fired { get { return Kind.Length > 0; } }
    }

    /// <summary>
    /// One moment the wall went up, and what the trader did about it.
    /// Stood == true means they stopped. Stood == false means they typed past it.
    /// </summary>
    public class TiltEvent
    {
        public DateTime At;
        public string AccountName = "";
        public string Kind = "";
        public bool Stood;

        /// <summary>The account's P&L for the day at the moment the wall went up.</summary>
        public double PnlAtEvent;
        /// <summary>Where that day finished. Tracked live, frozen at the date roll.</summary>
        public double PnlAfter;
        public bool Settled;

        /// <summary>
        /// Negative means carrying on cost money. Positive means it did not.
        /// Both are reported.
        /// </summary>
        public double Delta { get { return PnlAfter - PnlAtEvent; } }
    }

    /// <summary>
    /// The override record. Small, append-only, CSV, sits next to the journal.
    /// </summary>
    public class TiltLog
    {
        private readonly List<TiltEvent> items = new List<TiltEvent>();

        public int Count { get { return items.Count; } }
        public List<TiltEvent> All { get { return new List<TiltEvent>(items); } }

        public void Add(TiltEvent e)
        {
            if (e == null) return;
            e.PnlAfter = e.PnlAtEvent;
            items.Add(e);
        }

        public void Clear() { items.Clear(); }

        /// <summary>
        /// Keep today's unsettled events tracking the live daily P&L, and freeze
        /// anything left over from a previous date. Called every tick, so the cost
        /// of an override is correct at the end of the day without anyone having
        /// to remember to close the books.
        /// </summary>
        public bool Touch(string account, double dailyPnl, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return false;

            bool changed = false;

            for (int n = 0; n < items.Count; n++)
            {
                TiltEvent e = items[n];
                if (e.Settled) continue;
                if (!string.Equals(e.AccountName, account, StringComparison.OrdinalIgnoreCase)) continue;

                if (e.At.Date == now.Date)
                {
                    if (Math.Abs(e.PnlAfter - dailyPnl) > 0.004) { e.PnlAfter = dailyPnl; changed = true; }
                }
                else { e.Settled = true; changed = true; }
            }

            return changed;
        }

        /// <summary>Freeze everything older than today, whatever account it belongs to.</summary>
        public bool SettleStale(DateTime now)
        {
            bool changed = false;
            for (int n = 0; n < items.Count; n++)
                if (!items[n].Settled && items[n].At.Date != now.Date) { items[n].Settled = true; changed = true; }
            return changed;
        }

        public List<TiltEvent> Recent(DateTime now, int days)
        {
            List<TiltEvent> outp = new List<TiltEvent>();
            // Inclusive of today, so "last 30 days" is 30 days and not 31.
            DateTime cut = now.Date.AddDays(-(Math.Abs(days) - 1));
            for (int n = 0; n < items.Count; n++)
                if (items[n].At.Date >= cut) outp.Add(items[n]);
            return outp;
        }

        /// <summary>
        /// One day on one account, however many times the wall came up.
        ///
        /// These used to count EVENTS, and the difference is not academic: a
        /// trader who had used Ballast for two days was told he had stood down
        /// fourteen times in the last thirty. He had pressed the button fourteen
        /// times, because the wall came back on every restart - but he had made
        /// the decision twice, on two days, and the decision is what the number
        /// is supposed to be about.
        ///
        /// Counting sessions also makes the figure honest about what it can
        /// measure. Ballast sees a day and an account. It does not see fourteen
        /// separate resolutions.
        /// </summary>
        private static string SessionKey(TiltEvent e)
        {
            return e.At.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                 + "|" + (e.AccountName == null ? "" : e.AccountName.ToUpperInvariant());
        }

        public int OverrideCount(DateTime now, int days)
        {
            return SessionCount(now, days, false);
        }

        public int StoodCount(DateTime now, int days)
        {
            return SessionCount(now, days, true);
        }

        private int SessionCount(DateTime now, int days, bool stood)
        {
            List<TiltEvent> r = Recent(now, days);
            List<string> seen = new List<string>();

            for (int n = 0; n < r.Count; n++)
            {
                if (r[n].Stood != stood) continue;
                string k = SessionKey(r[n]);
                if (!seen.Contains(k)) seen.Add(k);
            }
            return seen.Count;
        }

        /// <summary>Overrides today, on this account. Drives the escalation copy.</summary>
        public int OverridesToday(string account, DateTime now)
        {
            int c = 0;
            for (int n = 0; n < items.Count; n++)
            {
                TiltEvent e = items[n];
                if (e.Stood) continue;
                if (e.At.Date != now.Date) continue;
                if (!string.Equals(e.AccountName, account, StringComparison.OrdinalIgnoreCase)) continue;
                c++;
            }
            return c;
        }

        /// <summary>
        /// The trader's own record, said back to them. Empty string when there is
        /// nothing to say — an empty history should show nothing at all rather
        /// than a reassuring line nobody earned.
        /// </summary>
        public string Summary(DateTime now, int days)
        {
            List<TiltEvent> r = Recent(now, days);

            // Grouped by session - one day on one account - because that is what
            // the sentence says. "Three sessions went on to lose" is a claim
            // about three days; counting each re-appearance of the wall as its
            // own session made it a claim about nothing.
            List<string> keys = new List<string>();
            List<double> sums = new List<double>();

            for (int n = 0; n < r.Count; n++)
            {
                if (r[n].Stood) continue;

                string k = SessionKey(r[n]);
                int at = keys.IndexOf(k);
                if (at < 0) { keys.Add(k); sums.Add(r[n].Delta); }
                else sums[at] = sums[at] + r[n].Delta;
            }

            int over = keys.Count, worse = 0, better = 0;
            double worseSum = 0, betterSum = 0, net = 0;

            for (int n = 0; n < sums.Count; n++)
            {
                double d = sums[n];
                net += d;
                if (d < 0) { worse++; worseSum += d; }
                else if (d > 0) { better++; betterSum += d; }
            }

            if (over == 0) return "";

            StringBuilder sb = new StringBuilder();

            if (over == 1)
            {
                sb.Append("You went past this once in the last ").Append(days).Append(" days. ");
                if (worse == 1) sb.Append("That session went on to lose a further ").Append(Money(-worseSum)).Append(".");
                else if (better == 1) sb.Append("That session went on to recover ").Append(Money(betterSum)).Append(".");
                else sb.Append("That session finished flat from there.");
                return sb.ToString();
            }

            sb.Append("You went past this ").Append(over).Append(" times in the last ")
              .Append(days).Append(" days. ");

            if (worse > 0)
            {
                if (worse == 1) sb.Append("One of those sessions went on to lose a further ").Append(Money(-worseSum));
                else sb.Append(worse).Append(" of those sessions went on to lose a further ").Append(Money(-worseSum));
                if (better > 0) sb.Append("; ").Append(better).Append(" recovered ").Append(Money(betterSum));
                sb.Append(". ");
            }
            else if (better > 0)
            {
                sb.Append("All ").Append(better).Append(" recovered, ").Append(Money(betterSum)).Append(" in total. ");
            }

            sb.Append("Net after carrying on: ").Append(Money(net)).Append(".");
            return sb.ToString();
        }

        /// <summary>Credit where it is due. Stopping is the behaviour worth growing.</summary>
        public string StoodSummary(DateTime now, int days)
        {
            int s = StoodCount(now, days);
            if (s == 0) return "";
            if (s == 1) return "You stood down once in the last " + days + " days.";
            return "You stood down " + s + " times in the last " + days + " days.";
        }

        // ── Persistence ──────────────────────────────────────────────────────

        public const string CsvHeader =
            "at,account,kind,stood,pnl_at_event,pnl_after,settled";

        public static string ToCsvLine(TiltEvent e)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(e.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(e.AccountName)).Append(',');
            sb.Append(Csv(e.Kind)).Append(',');
            sb.Append(e.Stood ? "1" : "0").Append(',');
            sb.Append(e.PnlAtEvent.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(e.PnlAfter.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(e.Settled ? "1" : "0");
            return sb.ToString();
        }

        public static TiltEvent FromCsvLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            List<string> f = SplitCsv(line);
            if (f.Count < 7) return null;

            TiltEvent e = new TiltEvent();
            DateTime at;
            if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) return null;
            e.At = at;
            e.AccountName = f[1];
            e.Kind = f[2];
            e.Stood = f[3] == "1";
            e.PnlAtEvent = Num(f[4]);
            e.PnlAfter = Num(f[5]);
            e.Settled = f[6] == "1";
            return e;
        }

        public bool Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(CsvHeader);
                for (int n = 0; n < items.Count; n++) sb.AppendLine(ToCsvLine(items[n]));
                AtomicFile.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public bool Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                string[] lines = File.ReadAllLines(path);
                items.Clear();
                for (int n = 1; n < lines.Length; n++)
                {
                    TiltEvent e = FromCsvLine(lines[n]);
                    if (e != null) items.Add(e);
                }
                return true;
            }
            catch { return false; }
        }

        private static double Num(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static List<string> SplitCsv(string line)
        {
            List<string> outp = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool q = false;

            for (int n = 0; n < line.Length; n++)
            {
                char c = line[n];
                if (q)
                {
                    if (c == '"')
                    {
                        if (n + 1 < line.Length && line[n + 1] == '"') { cur.Append('"'); n++; }
                        else q = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') q = true;
                    else if (c == ',') { outp.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            outp.Add(cur.ToString());
            return outp;
        }

        private static string Money(double n)
        {
            double r = Math.Round(n);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Which walls are currently down, and until when.
    ///
    /// A wall that reappears the instant it is dismissed is a cage, and a trader
    /// in a cage uninstalls the software. A wall that never comes back is
    /// decoration. So a release lasts a set number of minutes, and if the
    /// condition is still true when it lapses the wall comes back — and coming
    /// back costs another ten seconds and another line in the record.
    /// </summary>
    public class TiltGate
    {
        private readonly Dictionary<string, DateTime> until =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long a typed override buys. Deliberately short.</summary>
        public int ReleaseMinutes = 15;

        private static string Key(string account, string kind)
        {
            return (account ?? "") + "|" + (kind ?? "");
        }

        /// <summary>Key used by a release that covers the whole account.</summary>
        private const string AllKinds = "*";

        public void Release(string account, string kind, DateTime now)
        {
            Release(account, kind, now, ReleaseMinutes);
        }

        public void Release(string account, string kind, DateTime now, int minutes)
        {
            if (string.IsNullOrEmpty(account)) return;
            until[Key(account, kind)] = now.AddMinutes(Math.Max(0, minutes));
        }

        /// <summary>A configuration escape: quiet for the rest of the day, this reason only.</summary>
        public void ReleaseForDay(string account, string kind, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;
            until[Key(account, kind)] = now.Date.AddDays(1);
        }

        /// <summary>
        /// Standing down. Covers every reason on the account for the rest of the
        /// day, because that is what the button says it does - "leaves the
        /// account alone until tomorrow". Releasing only the one reason that
        /// happened to fire would put a fresh wall in front of someone who has
        /// already agreed to stop, which is how a good decision gets punished.
        /// </summary>
        public void ReleaseAccountForDay(string account, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;
            until[Key(account, AllKinds)] = now.Date.AddDays(1);
        }

        /// <summary>
        /// Undo a stand-down on one account.
        ///
        /// "I'm done for the day" is a decision, and a decision made by accident
        /// has to be reversible or the button becomes something a trader is
        /// afraid to press. Reversing it is deliberately not free: it clears
        /// every hold on the account, so the next line the account crosses raises
        /// its wall again from scratch. Nothing is forgotten, only un-silenced.
        /// </summary>
        public void ClearAccount(string account)
        {
            if (string.IsNullOrEmpty(account)) return;

            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in until)
            {
                int bar = kv.Key.IndexOf('|');
                string who = bar < 0 ? kv.Key : kv.Key.Substring(0, bar);
                if (string.Equals(who, account, StringComparison.OrdinalIgnoreCase))
                    gone.Add(kv.Key);
            }
            for (int i = 0; i < gone.Count; i++) until.Remove(gone[i]);
        }

        public bool IsReleased(string account, string kind, DateTime now)
        {
            DateTime any;
            if (until.TryGetValue(Key(account, AllKinds), out any) && now < any) return true;

            DateTime t;
            if (!until.TryGetValue(Key(account, kind), out t)) return false;
            return now < t;
        }

        public int MinutesLeft(string account, string kind, DateTime now)
        {
            DateTime t;
            if (!until.TryGetValue(Key(account, kind), out t)) return 0;
            double m = (t - now).TotalMinutes;
            return m <= 0 ? 0 : (int)Math.Ceiling(m);
        }

        public void Clear() { until.Clear(); }

        /// <summary>
        /// Everything still in force, as text, so it survives a restart.
        ///
        /// It did not, and that produced the worst possible behaviour: a trader
        /// pressed "I'm done for the day", Ballast agreed to leave the account
        /// alone until tomorrow, and then the next time the window opened the
        /// same wall was in front of him asking the same question. A promise the
        /// software forgets the moment it is restarted is not a promise, and
        /// being asked again is exactly the nag the wall is supposed to replace.
        /// </summary>
        public List<string> Serialise()
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in until)
            {
                lines.Add(kv.Key.Replace("\n", " ") + "|"
                        + kv.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            return lines;
        }

        /// <summary>
        /// Put back the releases that have not expired. Anything already past is
        /// dropped rather than loaded and ignored, so the file does not grow and
        /// a stale release can never be resurrected by a clock change.
        /// </summary>
        public void Restore(List<string> lines, DateTime now)
        {
            if (lines == null) return;

            for (int i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;

                // account|kind|until - the key itself contains one pipe.
                string[] f = lines[i].Split('|');
                if (f.Length < 3) continue;

                DateTime t;
                if (!DateTime.TryParse(f[f.Length - 1], CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out t)) continue;
                if (t <= now) continue;

                string key = string.Join("|", f, 0, f.Length - 1);
                until[key] = t;
            }
        }
    }

    public static class TiltLockout
    {
        /// <summary>
        /// The sentence. Chosen to be about ten seconds of typing, to be in the
        /// first person, and to name the actual stake — an account, not "a rule".
        /// Nobody types this out and still believes they are following a plan.
        /// </summary>
        public const string ReleaseSentence =
            "I am trading outside my plan and I accept I may lose this account.";

        /// <summary>Minutes a typed override buys before the wall may return.</summary>
        public const int DefaultReleaseMinutes = 15;

        // ── Matching ─────────────────────────────────────────────────────────

        /// <summary>
        /// Letters, digits and single spaces. Case, punctuation and stray spacing
        /// are all forgiven — fighting a text box is not the point, and a trader
        /// who has typed the sentence and is being told they got the apostrophe
        /// wrong will hate this feature rather than hear it.
        /// </summary>
        public static string Normalize(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            bool space = true;

            for (int n = 0; n < s.Length; n++)
            {
                char c = s[n];
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
                else if (char.IsWhiteSpace(c)) { if (!space) { sb.Append(' '); space = true; } }
                // punctuation is dropped entirely
            }

            return sb.ToString().Trim();
        }

        public static bool Accepts(string typed)
        {
            string t = Normalize(typed);
            return t.Length > 0 && t == Normalize(ReleaseSentence);
        }

        /// <summary>True while what has been typed is still on track for the sentence.</summary>
        public static bool OnTrack(string typed)
        {
            string t = Normalize(typed);
            if (t.Length == 0) return true;
            return Normalize(ReleaseSentence).StartsWith(t, StringComparison.Ordinal);
        }

        /// <summary>
        /// 0..1 progress through the sentence, for a quiet bar under the box.
        /// A trader typing in a temper needs to see it is working.
        /// </summary>
        public static double Progress(string typed)
        {
            string target = Normalize(ReleaseSentence);
            string t = Normalize(typed);
            if (target.Length == 0) return 1;
            if (!OnTrack(typed)) return 0;
            double p = (double)t.Length / target.Length;
            return p > 1 ? 1 : p;
        }

        // ── Triggering ───────────────────────────────────────────────────────

        private static bool Has(List<RiskSignal> signals, string key)
        {
            if (signals == null) return false;
            for (int n = 0; n < signals.Count; n++) if (signals[n].Key == key) return true;
            return false;
        }

        /// <summary>
        /// Decide whether this account, right now, deserves a wall.
        ///
        /// Only the hard breakers qualify. Being over your trade count or outside
        /// your window is worth a line on a row; it is not worth taking the screen
        /// away. A wall that fires for small things stops meaning anything.
        /// </summary>
        public static TiltTrigger Evaluate(string accountName, DisciplineInput i,
                                           DisciplineDecision d, bool includeGiveBack)
        {
            List<TiltTrigger> all = EvaluateAll(accountName, i, d, includeGiveBack);
            return all.Count > 0 ? all[0] : Empty(accountName);
        }

        /// <summary>
        /// Every reason this account deserves a wall, worst first.
        ///
        /// A list rather than a single winner because the caller may have to skip
        /// the top one. If a trader has told Ballast its floor figure is wrong and
        /// dismissed it for the day, that must silence the floor warning and
        /// nothing else - it must not quietly disarm the daily loss limit on the
        /// same account for the rest of the session.
        /// </summary>
        public static List<TiltTrigger> EvaluateAll(string accountName, DisciplineInput i,
                                                    DisciplineDecision d, bool includeGiveBack)
        {
            List<TiltTrigger> outp = new List<TiltTrigger>();
            if (i == null || d == null) return outp;
            if (!i.HasValidEquity) return outp;      // no data is not a reason to shout

            // A strategy does not tilt. Every word on that wall is addressed to a
            // person about to take a trade to get even, and a bot grinding a
            // play account below its floor is not that - it is just a bot. The
            // risk figures still apply and still show on the row and the chart;
            // what is switched off is the argument, because there is nobody there
            // to have it with.
            if (i.IsAutomated) return outp;

            // Nothing has happened yet, so nothing can have gone wrong yet.
            //
            // "this is the message i received when i opened up my ninjatrader
            // this morning...havent placed a trade or even been on ninjatrader
            // yet"
            //
            // The cause was a feed carrying yesterday's realised P&L into the
            // morning, and that is fixed where it belongs. This is the guard
            // that should have caught it anyway: every wall in here is addressed
            // to somebody about to take a trade to get even, and none of it can
            // be true of a flat account that has not traded today. Whatever
            // arithmetic produced a red screen on an untouched morning, it is
            // wrong - the wall is the loudest thing Ballast owns and it must
            // never be the first thing a man sees on a day he has not started.
            //
            // The row and the chart still say whatever they say. This only stops
            // the argument, because there is nobody to have it with.
            if (i.TradesToday <= 0 && i.OpenContracts == 0) return outp;

            string name = accountName ?? "";

            if (Has(d.Signals, TiltKind.PastFloor))
            {
                TiltTrigger t = New(name, TiltKind.PastFloor, i);
                t.ConfigSuspect = true;
                t.Title = "This account has no room left.";
                t.Line = name + " is at " + Money(i.CurrentEquity)
                       + ", at or below its floor of " + Money(i.FloorLevel) + ".";
                t.Ask = "If this is a live funded account, it is over. If that number looks wrong, "
                      + "the account size in Setup does not match this account - fix that instead of trading.";
                outp.Add(t);
            }

            // Only while the account is ACTUALLY down past the limit right now.
            //
            // The signal itself is latched for the rest of the day once the limit
            // has been reached - winning some back does not give the day back,
            // and the account's advice stays red saying so. The WALL is for the
            // acute moment, though. Throwing it again every fifteen minutes for
            // the rest of a session the trader has already typed their way past
            // would turn the one thing in Ballast that is supposed to stop
            // somebody into wallpaper, and the next wall - the one for a real
            // breach - would be dismissed on reflex.
            if (Has(d.Signals, TiltKind.DailyLossLimit)
                && i.DailyLossLimit > 0 && i.DailyPnl <= -Math.Abs(i.DailyLossLimit))
            {
                TiltTrigger t = New(name, TiltKind.DailyLossLimit, i);
                t.Title = "You are done for the day.";
                t.Line = name + " is down " + Money(-i.DailyPnl)
                       + " - past the " + Money(i.DailyLossLimit) + " you set as your limit.";
                t.Ask = "Nothing from here is a setup. It is a bet to get even, and it is being placed by the "
                      + "part of you that just lost " + Money(-i.DailyPnl) + ".";
                outp.Add(t);
            }

            if (Has(d.Signals, TiltKind.LossStreak))
            {
                TiltTrigger t = New(name, TiltKind.LossStreak, i);
                t.Title = "You are done for the day.";
                t.Line = name + " has taken " + i.LossStreak
                       + (i.LossStreak == 1 ? " loss" : " losses") + " in a row"
                       + " - you said " + i.MaxLossesBeforeStop + " was your line.";
                t.Ask = "You drew that line when you were calm. You are not calm now, "
                      + "so this is not the moment to move it.";
                outp.Add(t);
            }

            // "didnt even see the warning to stop."
            //
            // He had set five trades and taken six. Everything Ballast is
            // supposed to do had happened: the row went amber, the action said
            // DONE TODAY, the chart carried "6 TRADES - AT YOUR LIMIT". None of
            // it interrupted him, because none of it was different from what the
            // chart says all day. The count was the only line he had drawn that
            // could be walked through without anything standing in the way.
            //
            // There was no principle behind that, only an omission. He picks the
            // number of losses that ends his day and gets a wall; he picks the
            // number of trades that ends his day and got a colour. Both are
            // lines drawn by the calm version of him, and the wall exists
            // precisely for the moment the calm version is not the one at the
            // keyboard.
            if (Has(d.Signals, TiltKind.MaxTrades) && i.MaxTrades > 0)
            {
                TiltTrigger t = New(name, TiltKind.MaxTrades, i);
                t.Title = "You are done for the day.";
                t.Line = name + " has taken " + i.TradesToday
                       + (i.TradesToday == 1 ? " trade" : " trades")
                       + " - you said " + i.MaxTrades + " was your limit.";
                t.Ask = "A trade count is not about this trade being bad. It is the number you "
                      + "picked because past it you stop choosing setups and start taking them. "
                      + "You are past it.";
                outp.Add(t);
            }

            // The give-back signal fires on how much of a peak has been handed
            // back, which can happen on a day that has since gone red. Telling
            // someone they are "still up -$200" is exactly the kind of visibly
            // false line that makes a trader stop believing the rest of it, so
            // this wall only appears while there is genuinely something left to
            // walk away with. Once the day is red the loss-limit wall is the
            // right one anyway.
            if (includeGiveBack && Has(d.Signals, TiltKind.GiveBack) && i.DailyPnl > 0)
            {
                TiltTrigger t = New(name, TiltKind.GiveBack, i);
                t.Title = "You are handing back a green day.";
                t.Line = name + " was up " + Money(i.PeakDailyPnl)
                       + " and has given back " + Money(i.PeakDailyPnl - i.DailyPnl)
                       + ". You are still up " + Money(i.DailyPnl) + ".";
                t.Ask = "That money is still on the table. You can still walk away with it, "
                      + "and you will not get a second chance to do that today.";
                outp.Add(t);
            }

            // The consistency ceiling. Last, because everything above it is a
            // day going wrong and this is a day going right - and a trader who
            // is also past his loss limit does not need to hear about payout
            // paperwork first.
            if (Has(d.Signals, TiltKind.Windfall) && i.DailyPnl > 0 && i.WindfallCeiling > 0)
            {
                TiltTrigger t = New(name, TiltKind.Windfall, i);
                t.Title = "This day is big enough to hold up your payout.";
                t.Line = name + " is up " + Money(i.DailyPnl) + " against a ceiling of "
                       + Money(i.WindfallCeiling) + ". Your firm will not release a payout while "
                       + "one day is that large a share of your total profit, and clearing it now "
                       + "needs " + Money(i.ProfitToUnblockPayout) + " more profit on other days.";
                t.Ask = "Nothing is lost here - the payout is postponed, not forfeited, and more "
                      + "trading days dilute it. But this is the one rule where the good day is "
                      + "the problem, and stopping now is what turns it into money you can "
                      + "actually withdraw.";
                outp.Add(t);
            }

            return outp;
        }

        /// <summary>
        /// True for the reasons that mean the account has broken a hard line, as
        /// opposed to the softer give-back warning. Only these light up the chart.
        /// </summary>
        public static bool IsHardBreaker(string kind)
        {
            return kind == TiltKind.PastFloor
                || kind == TiltKind.DailyLossLimit
                || kind == TiltKind.LossStreak
                || kind == TiltKind.MaxTrades;
        }

        private static TiltTrigger New(string name, string kind, DisciplineInput i)
        {
            TiltTrigger t = new TiltTrigger();
            t.AccountName = name;
            t.Kind = kind;
            t.DailyPnl = i.DailyPnl;
            return t;
        }

        private static TiltTrigger Empty(string accountName)
        {
            TiltTrigger t = new TiltTrigger();
            t.AccountName = accountName ?? "";
            return t;
        }

        /// <summary>
        /// The line that escalates without nagging: what carrying on has already
        /// done today, on this account. Empty when this is the first time.
        /// </summary>
        public static string TodayLine(TiltLog log, string account, DateTime now)
        {
            if (log == null) return "";
            int n = log.OverridesToday(account, now);
            if (n <= 0) return "";
            if (n == 1) return "This is the second time today.";
            return "This is time number " + (n + 1) + " today.";
        }

        private static string Money(double n)
        {
            double r = Math.Round(n);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
