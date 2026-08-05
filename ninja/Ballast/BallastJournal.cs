// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastJournal.cs
//
// The journal most traders abandon fails for a mechanical reason: it asks them
// to retype what the platform already knows. Ticker, direction, size, entry and
// exit time, duration, P&L — all of that is sitting in NinjaTrader already, and
// asking a person to copy it by hand is how a journal quietly dies in week two.
//
// So Ballast writes all of that itself, and asks the trader for the two things
// software genuinely cannot see: was that trade your plan, and what were you
// feeling. Everything else is captured, including the part no other journal has:
// WHAT BALLAST WAS ADVISING AT THE MOMENT OF ENTRY. That single field is what
// turns a log into evidence, because at the end of a month it can say "you took
// 14 trades after Ballast told you to stop, and they cost you $3,200."
//
// Design choices are deliberate and evidence-led rather than copied from other
// journal products:
//
//   * Capture at the close, not at the end of the day. Memory of one's own
//     reasoning is reconstructed to fit the outcome — a loser becomes "I knew
//     it was wrong", a winner becomes "I planned that". Tagging minutes later
//     records what actually happened; tagging at 4pm records a story.
//
//   * Feelings are PICKED FROM A FIXED LIST, never typed. Affect labelling
//     research finds that selecting a provided label produces an immediate drop
//     in emotional intensity, whereas generating your own wording only pays off
//     days later and can raise distress first. A dropdown is also one tap, and
//     friction is what kills journals.
//
//   * One binary question — planned or not — carries most of the value. It is
//     the process/outcome split: it grades the DECISION rather than the result,
//     which is the only way a good trade that lost and a reckless trade that
//     won get scored honestly.
//
//   * Nothing is mandatory. An untagged trade is still a complete record, and
//     the queue can be cleared whenever the trader feels like it. A journal that
//     nags gets closed.
//
// Pure C# — no NinjaTrader types — so all of this is unit tested.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ballast
{
    public class BallastTrade
    {
        // ── Captured automatically. The trader never types any of this.
        public string AccountName = "";
        public string Instrument = "";
        public bool IsLong;
        public int MaxContracts;
        public DateTime EntryTime;
        public DateTime ExitTime;
        public double Pnl;

        /// <summary>
        /// What this round trip cost in commission, taken from the account's own
        /// running total across the trade. 0 when it is not known.
        ///
        /// It is recorded for its own sake - a trader taking four trades a day on
        /// ten minis is paying real money and ought to see it - but it also has a
        /// job. The account's realised P&L and Ballast's round-trip figures do
        /// not always post commission at the same instant, so they drift by a few
        /// dollars per contract. Knowing what the watched trades cost puts a
        /// bound on that drift, instead of a flat guess that would be far too
        /// small for someone trading size and far too big for someone trading
        /// one lot.
        /// </summary>
        public double Commission;

        // ── Context at the moment of entry. This is the interesting half.
        public int TradeNumberToday;
        public double DailyPnlBefore;
        public double CushionAtEntry;
        public double FloorAtEntry;
        public int MinutesSincePreviousLoss = -1;   // -1 == no loss yet today
        public bool PreviousTradeWasLoss;
        public bool InsideSessionWindow;

        /// <summary>What Ballast was advising when this trade was opened.</summary>
        public string AdviceAtEntry = "";

        /// <summary>The if-then plan set for the day, copied onto every trade.</summary>
        public string SessionPlan = "";

        // ── The only things we ask a human for.
        /// <summary>"", "planned" or "unplanned".</summary>
        public string Planned = "";
        /// <summary>"" or one of BallastJournal.Feelings.</summary>
        public string Feeling = "";
        public string Note = "";

        /// <summary>
        /// Which of the trader's own setups this trade was — "" or one of the
        /// setups in their SetupBook (ballast-setups.txt).
        ///
        /// This is the field the edge experiment turns on. Everything else the
        /// journal captures answers "how did you behave"; this one answers "and
        /// which strategy was it", which is the only way A's expectancy can be
        /// told from B's. Left blank rather than guessed, exactly like Feeling: a
        /// setup label invented after the fact would manufacture the very evidence
        /// the trader is trying to earn.
        /// </summary>
        public string Setup = "";

        /// <summary>
        /// Whether the stop or the target was moved once the trade was on, and
        /// which. "" means not answered.
        ///
        /// This is the one thing software genuinely cannot see - Ballast watches
        /// the position, not the working orders - and it is the discipline break
        /// that costs the most. A stop moved away from price converts a planned
        /// loss into an unplanned one, and a target pulled in converts a winner
        /// into a scratch. Asking about it turns the most expensive habit in
        /// trading into something with a number attached.
        /// </summary>
        public string Moved = "";

        // ── Photographs of the chart, taken automatically.
        /// <summary>PNG of the chart when this trade was opened. "" if none.</summary>
        public string EntryImage = "";
        /// <summary>PNG of the chart when it was closed. "" if none.</summary>
        public string ExitImage = "";

        public bool HasImages { get { return EntryImage.Length > 0 || ExitImage.Length > 0; } }

        /// <summary>
        /// The trader has finished with this trade and cleared it from the strip.
        /// Separate from IsTagged on purpose: the verdict is captured on the first
        /// tap, but the row has to stay put afterwards so a feeling and a note can
        /// still be added. Clearing the row the instant the verdict landed made the
        /// other two fields unreachable.
        /// </summary>
        public bool Dismissed;

        /// <summary>Taken by a strategy rather than by hand.</summary>
        public bool Automated;

        public bool IsWin { get { return Pnl > 0; } }
        public bool IsTagged { get { return Planned.Length > 0; } }

        public double DurationMinutes
        {
            get
            {
                double m = (ExitTime - EntryTime).TotalMinutes;
                return m < 0 ? 0 : m;
            }
        }

        /// <summary>
        /// True when Ballast had already said stop, cool off or protect, and the
        /// trade was opened regardless. The single most diagnostic flag here.
        /// </summary>
        public bool TakenAgainstAdvice
        {
            get
            {
                return AdviceAtEntry == "Lockout"
                    || AdviceAtEntry == "StopForDay"
                    || AdviceAtEntry == "Cooldown"
                    || AdviceAtEntry == "ProtectGreen";
            }
        }

        /// <summary>
        /// True for a row Ballast reconstructed from the account rather than
        /// watched. It has no direction and no size - it is a difference, not a
        /// position - so the places that print "Long 2 NQ SEP26" have to print
        /// something else for it.
        /// </summary>
        public bool IsReconstructed { get { return MaxContracts <= 0; } }

        public string DirectionLabel
        {
            get { return IsReconstructed ? "" : (IsLong ? "Long" : "Short"); }
        }

        /// <summary>"Long 2" for a watched trade, and nothing at all for a reconstructed one.</summary>
        public string SizeLabel
        {
            get
            {
                if (IsReconstructed) return "";
                return DirectionLabel + " " + MaxContracts;
            }
        }

        /// <summary>One-line description for the pending-tag strip.</summary>
        public string ShortLabel
        {
            get
            {
                string ins = Instrument.Length > 0 ? Instrument : "position";
                string size = SizeLabel;
                return (size.Length > 0 ? size + " " : "") + ins
                     + "  " + EntryTime.ToString("HH:mm", CultureInfo.InvariantCulture)
                     + "-" + ExitTime.ToString("HH:mm", CultureInfo.InvariantCulture)
                     + "  " + Money(Pnl);
            }
        }

        public static string Money(double v)
        {
            string s = Math.Abs(v).ToString("N0", CultureInfo.InvariantCulture);
            return (v < 0 ? "-$" : "$") + s;
        }
    }

    /// <summary>A group of trades reduced to the numbers worth looking at.</summary>
    public class JournalBucket
    {
        public string Label = "";
        public int Count;
        public int Wins;
        public double Net;
        public double GrossWin;
        public double GrossLoss;   // positive number

        public double WinRate { get { return Count == 0 ? 0 : (double)Wins / Count; } }
        public double Average { get { return Count == 0 ? 0 : Net / Count; } }

        public void Add(BallastTrade e)
        {
            Count++;
            Net += e.Pnl;
            if (e.Pnl > 0) { Wins++; GrossWin += e.Pnl; }
            else GrossLoss += -e.Pnl;
        }
    }

    /// <summary>
    /// How confident Ballast is that a setup's positive result is a real edge
    /// rather than a lucky run. Ordered worst-to-best so callers can compare.
    /// </summary>
    public enum EdgeConfidence { TooFew, NoEdge, InTheNoise, ProbablyReal, LikelyReal }

    /// <summary>
    /// A setup's expectancy read: the numbers, and whether the result can be told
    /// apart from luck. This one object is the entire point of the experiment -
    /// the honest answer to "does this setup actually make money, or have I just
    /// not lost with it yet?"
    /// </summary>
    public class EdgeReadResult
    {
        public int Count;
        public int Wins;
        public double WinRate;
        public double Expectancy;   // net $ per trade, after commission
        public double Total;        // net $ across the whole sample
        public double TStat;        // one-sample t of net-per-trade against zero
        public EdgeConfidence Confidence = EdgeConfidence.TooFew;
        public string Verdict = "";
    }

    public class BallastJournal
    {
        /// <summary>
        /// Fixed labels, chosen so a trader can hit one without deliberating.
        ///
        /// Picked from a list rather than typed, on purpose. Naming a feeling
        /// from a short set is a second's work and produces something countable;
        /// a free text box produces prose nobody can tally and most people leave
        /// empty. The cost is that a state with no label here goes unrecorded,
        /// which is why the list is longer than the original six.
        ///
        /// Ordered roughly best-to-worst rather than alphabetically, so the eye
        /// lands where it usually needs to. Several are deliberately kept apart
        /// even though they overlap, because the BEHAVIOUR differs:
        ///
        ///   "Wanted it back" is not "Angry" - chasing a loss is its own thing.
        ///   "Afraid to miss it" is not "Impatient" - one is about the market
        ///   leaving, the other about being bored of waiting for it.
        ///   "Confident" is not "Invincible" - the second one is the tell that
        ///   shows up right before a size-up.
        ///   "Hesitant" is not "Unsure" - hesitating means you knew and did not
        ///   act; unsure means you did not know.
        ///
        /// Adding to this list is safe: journal rows store the label as text, so
        /// old entries keep whatever they were tagged with.
        /// </summary>
        public static readonly string[] Feelings = new string[]
        {
            "Calm",
            "Focused",
            "Confident",
            "Invincible",
            "Relieved",
            "Unsure",
            "Hesitant",
            "Impatient",
            "Bored",
            "Distracted",
            "Tired",
            "Rushed",
            "Anxious",
            "Afraid to miss it",
            "Scared to lose",
            "Frustrated",
            "Angry",
            "Wanted it back",
            "Numb"
        };

        /// <summary>
        /// The seed a brand-new setup book starts from. Empty on purpose: Ballast
        /// never invents setups for a trader — every trader trades differently, so
        /// each defines their own, stored per-trader in ballast-setups.txt and
        /// managed by <see cref="SetupBook"/>. Kept as a named constant so a
        /// specific deployment has one obvious place to add starter examples if it
        /// ever wants them.
        ///
        /// Setup labels are stored as text on each row, so editing a trader's book
        /// never rewrites history — a retired setup's old trades keep their label.
        /// </summary>
        public static readonly string[] Setups = new string[] { };

        /// <summary>
        /// Four verdicts, not two.
        ///
        /// "Planned or not" conflated two different questions: did you take a
        /// setup you actually trade, and did you execute it the way you meant to.
        /// A trader can pick the right setup and still chase the entry, size up,
        /// or move a stop - and calling that "planned" buries the only thing worth
        /// knowing about it.
        ///
        /// Still one tap. The friction is unchanged; only the resolution went up.
        ///
        /// The vocabulary is FIXED rather than user-defined on purpose. Everyone
        /// inventing their own labels would make "your unplanned trades cost you
        /// $X" impossible to compute, and that sentence is the point of the
        /// journal.
        /// </summary>
        public static readonly string[] PlannedOptions = new string[]
        {
            Verdict_ByTheBook, Verdict_Sloppy, Verdict_OffPlan, Verdict_Chased
        };

        public const string Verdict_ByTheBook = "planned";          // was in the plan, executed as planned
        public const string Verdict_Sloppy    = "planned_sloppy";   // right setup, broke a rule taking it
        public const string Verdict_OffPlan   = "unplanned";        // not a setup you trade
        public const string Verdict_Chased    = "chased";           // took it to win something back

        /// <summary>Short label for a verdict, for buttons and rows.</summary>
        public const string Moved_Nothing = "held";
        public const string Moved_Stop    = "moved stop";
        public const string Moved_Target  = "moved target";
        public const string Moved_Both    = "moved both";

        public static readonly string[] MovedOptions = new string[]
        {
            Moved_Nothing, Moved_Stop, Moved_Target, Moved_Both
        };

        public static string MovedLabel(string m)
        {
            if (m == Moved_Nothing) return "held both";
            if (m == Moved_Stop)    return "moved my stop";
            if (m == Moved_Target)  return "moved my target";
            if (m == Moved_Both)    return "moved both";
            return "not said";
        }

        /// <summary>True when the trader moved something after the trade was on.</summary>
        public static bool DidMove(string m)
        {
            return m == Moved_Stop || m == Moved_Target || m == Moved_Both;
        }

        public static string VerdictLabel(string v)
        {
            if (v == Verdict_ByTheBook) return "by the book";
            if (v == Verdict_Sloppy)    return "right idea, sloppy";
            if (v == Verdict_OffPlan)   return "off plan";
            if (v == Verdict_Chased)    return "chased it";
            return "untagged";
        }

        /// <summary>
        /// Was the SETUP one the trader actually trades? The two "planned"
        /// verdicts both answer yes. This is what keeps every existing statistic,
        /// and every row already written to the CSV, working unchanged.
        /// </summary>
        public static bool IsPlannedVerdict(string v)
        {
            return v == Verdict_ByTheBook || v == Verdict_Sloppy;
        }

        public static bool IsUnplannedVerdict(string v)
        {
            return v == Verdict_OffPlan || v == Verdict_Chased;
        }

        private readonly List<BallastTrade> entries = new List<BallastTrade>();

        /// <summary>
        /// The day's if-then plan. Implementation-intention research is unusually
        /// consistent that a specific "if X happens, I will do Y" plan beats a
        /// general intention, because it hands control to the situation rather
        /// than to willpower at the moment it is weakest.
        /// </summary>
        public string SessionPlan = "";

        public int Count { get { return entries.Count; } }
        public List<BallastTrade> All { get { return new List<BallastTrade>(entries); } }

        public void Add(BallastTrade e)
        {
            if (e == null) return;
            if (e.SessionPlan.Length == 0) e.SessionPlan = SessionPlan;
            entries.Add(e);
        }

        /// <summary>
        /// Collapse reconstructed rows to one per account per day, summing what
        /// they carry. Returns how many rows were removed.
        ///
        /// A reconstructed row is Ballast's measurement of what it could not
        /// watch, and it re-measures every time it opens. Twelve restarts once
        /// produced twelve rows describing the same gap - and once those rows
        /// count as trades, twelve of them is an account at a limit its owner
        /// never went near. Only one can be true, so only one is kept, and the
        /// money is added up rather than thrown away.
        ///
        /// Run on load, so a journal written by an older build is corrected once
        /// rather than carried.
        /// </summary>
        public int ConsolidateReconstructed()
        {
            List<string> keys = new List<string>();
            List<BallastTrade> keep = new List<BallastTrade>();
            List<BallastTrade> outp = new List<BallastTrade>();
            int removed = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                BallastTrade e = entries[i];
                if (e == null) continue;

                if (!e.IsReconstructed) { outp.Add(e); continue; }

                string k = e.ExitTime.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                         + "|" + (e.AccountName == null ? "" : e.AccountName.ToUpperInvariant());

                int at = keys.IndexOf(k);
                if (at < 0)
                {
                    keys.Add(k);
                    keep.Add(e);
                    outp.Add(e);
                    continue;
                }

                // Fold into the one already kept: the earliest start, the latest
                // finish, and every dollar.
                BallastTrade first = keep[at];
                first.Pnl += e.Pnl;
                first.Commission += e.Commission;
                if (e.EntryTime < first.EntryTime) first.EntryTime = e.EntryTime;
                if (e.ExitTime > first.ExitTime) first.ExitTime = e.ExitTime;
                removed++;
            }

            if (removed > 0)
            {
                entries.Clear();
                entries.AddRange(outp);
            }
            return removed;
        }

        public void Clear() { entries.Clear(); }

        /// <summary>Trades still waiting for a tag, oldest first.</summary>
        public List<BallastTrade> Untagged()
        {
            List<BallastTrade> list = new List<BallastTrade>();
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].Automated && !entries[i].IsTagged) list.Add(entries[i]);
            return list;
        }

        /// <summary>
        /// What the tag strip shows: anything the trader has not finished with,
        /// whether or not a verdict has been recorded yet.
        /// </summary>
        public List<BallastTrade> Pending()
        {
            List<BallastTrade> list = new List<BallastTrade>();
            for (int i = 0; i < entries.Count; i++)
            {
                // Nothing to ask a strategy. Queueing bot trades would bury the
                // two or three discretionary ones that actually need an answer.
                if (entries[i].Automated) continue;
                if (!entries[i].Dismissed) list.Add(entries[i]);
            }
            return list;
        }

        public List<BallastTrade> ForDay(DateTime day)
        {
            List<BallastTrade> list = new List<BallastTrade>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].ExitTime.Date == day.Date) list.Add(entries[i]);
            return list;
        }

        /// <summary>
        /// Trades for a set of accounts, newest first. Used by the journal list so
        /// it can show only the accounts currently being watched.
        /// </summary>
        public List<BallastTrade> ForAccounts(List<string> accounts, bool todayOnly, DateTime today)
        {
            List<BallastTrade> list = new List<BallastTrade>();
            if (accounts == null) return list;

            for (int i = 0; i < entries.Count; i++)
            {
                BallastTrade e = entries[i];

                bool wanted = false;
                for (int a = 0; a < accounts.Count; a++)
                {
                    if (string.Equals(e.AccountName, accounts[a], StringComparison.OrdinalIgnoreCase))
                    { wanted = true; break; }
                }
                if (!wanted) continue;
                if (todayOnly && e.ExitTime.Date != today.Date) continue;

                list.Add(e);
            }

            // Newest first: the trade you are thinking about is the last one.
            list.Sort(delegate(BallastTrade a, BallastTrade b) { return b.ExitTime.CompareTo(a.ExitTime); });
            return list;
        }

        /// <summary>How many recorded trades belong to accounts NOT in this list.</summary>
        public int CountOutside(List<string> accounts)
        {
            int n = 0;
            if (accounts == null) return entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                bool found = false;
                for (int a = 0; a < accounts.Count; a++)
                {
                    if (string.Equals(entries[i].AccountName, accounts[a], StringComparison.OrdinalIgnoreCase))
                    { found = true; break; }
                }
                if (!found) n++;
            }
            return n;
        }

        public List<BallastTrade> ForAccount(string account)
        {
            List<BallastTrade> list = new List<BallastTrade>();
            if (string.IsNullOrEmpty(account)) return list;
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].AccountName, account, StringComparison.OrdinalIgnoreCase))
                    list.Add(entries[i]);
            return list;
        }

        // ── The comparisons that make it a journal rather than a log ─────────

        /// <summary>
        /// Only the trades a person actually decided to take.
        ///
        /// Every discipline statistic runs through this. A strategy's trades are
        /// real money and belong in the P&L, but they say nothing about whether
        /// the trader is behaving - and mixed in, a busy bot would swamp the
        /// signal entirely.
        /// </summary>
        public static List<BallastTrade> ManualOnly(List<BallastTrade> source)
        {
            List<BallastTrade> list = new List<BallastTrade>();
            if (source == null) return list;
            for (int i = 0; i < source.Count; i++)
                if (!source[i].Automated) list.Add(source[i]);
            return list;
        }

        public static List<BallastTrade> AutomatedOnly(List<BallastTrade> source)
        {
            List<BallastTrade> list = new List<BallastTrade>();
            if (source == null) return list;
            for (int i = 0; i < source.Count; i++)
                if (source[i].Automated) list.Add(source[i]);
            return list;
        }

        /// <summary>
        /// Planned versus unplanned. Untagged trades are excluded rather than
        /// assumed either way: guessing here would manufacture the very insight
        /// the trader is supposed to earn.
        /// </summary>
        public List<JournalBucket> PlannedSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);
            JournalBucket planned = new JournalBucket(); planned.Label = "Planned";
            JournalBucket unplanned = new JournalBucket(); unplanned.Label = "Unplanned";

            for (int i = 0; i < source.Count; i++)
            {
                if (IsPlannedVerdict(source[i].Planned)) planned.Add(source[i]);
                else if (IsUnplannedVerdict(source[i].Planned)) unplanned.Add(source[i]);
            }

            List<JournalBucket> list = new List<JournalBucket>();
            list.Add(planned); list.Add(unplanned);
            return list;
        }

        /// <summary>
        /// The four verdicts separately. Where PlannedSplit answers "did you take
        /// your setup", this answers "and did you take it properly" - which is a
        /// different leak with a different fix.
        /// </summary>
        public List<JournalBucket> VerdictSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);

            List<JournalBucket> list = new List<JournalBucket>();
            for (int v = 0; v < PlannedOptions.Length; v++)
            {
                JournalBucket b = new JournalBucket();
                b.Label = VerdictLabel(PlannedOptions[v]);

                for (int i = 0; i < source.Count; i++)
                    if (source[i].Planned == PlannedOptions[v]) b.Add(source[i]);

                if (b.Count > 0) list.Add(b);
            }
            return list;
        }

        public List<JournalBucket> FeelingSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);
            Dictionary<string, JournalBucket> map = new Dictionary<string, JournalBucket>();
            List<JournalBucket> order = new List<JournalBucket>();

            for (int f = 0; f < Feelings.Length; f++)
            {
                JournalBucket b = new JournalBucket();
                b.Label = Feelings[f];
                map[Feelings[f]] = b;
                order.Add(b);
            }

            for (int i = 0; i < source.Count; i++)
            {
                JournalBucket b;
                if (source[i].Feeling.Length > 0 && map.TryGetValue(source[i].Feeling, out b))
                    b.Add(source[i]);
            }

            // Only hand back feelings that actually occurred.
            List<JournalBucket> used = new List<JournalBucket>();
            for (int i = 0; i < order.Count; i++)
                if (order[i].Count > 0) used.Add(order[i]);
            return used;
        }

        /// <summary>
        /// Money and win rate per setup, worst net first.
        ///
        /// This is the split the edge experiment lives on. It is the only one that
        /// answers "which of the setups I actually trade is carrying me, and which
        /// is a leak wearing a strategy's clothes" - the question ten years of
        /// blended P&L never had to answer.
        ///
        /// Manual trades only, and an untagged trade is left out rather than
        /// pooled: a strategy has no setup to report, and guessing which setup a
        /// blank row was would manufacture the very number the trader is trying to
        /// earn. Worst first, like the instrument split, so the setup costing
        /// money is the first thing seen rather than the last.
        /// </summary>
        public List<JournalBucket> SetupSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);
            Dictionary<string, JournalBucket> map = new Dictionary<string, JournalBucket>();
            List<JournalBucket> list = new List<JournalBucket>();

            for (int i = 0; i < source.Count; i++)
            {
                string key = source[i].Setup;
                if (key == null || key.Length == 0) continue;   // untagged: never guessed

                JournalBucket b;
                if (!map.TryGetValue(key, out b))
                {
                    b = new JournalBucket();
                    b.Label = key;
                    map[key] = b;
                    list.Add(b);
                }
                b.Add(source[i]);
            }

            list.Sort(delegate(JournalBucket a, JournalBucket c) { return a.Net.CompareTo(c.Net); });
            return list;
        }

        /// <summary>
        /// The honest read on a set of trades: expectancy after commission, and a
        /// one-sample t-test of the per-trade result against zero, turned into a
        /// plain-English verdict.
        ///
        /// Measured NET of the round-trip commission each trade recorded, because a
        /// green gross number that goes red after costs is the single most common
        /// way a trader talks themselves into an edge they do not have. Rows that
        /// never learned their commission - an older journal, a feed that did not
        /// report it - count it as zero rather than inventing a figure.
        ///
        /// A t-stat, not a bare average, because the average alone cannot tell a
        /// real edge from a lucky streak: the same +$40 a trade means one thing
        /// over 12 wild trades and another over 60 steady ones. Below the minimum
        /// sample it refuses to answer at all - a verdict drawn from six trades is
        /// worse than none, because the trader will believe it and act on it.
        ///
        /// Pure arithmetic, no NinjaTrader types, so every branch here is unit
        /// tested against known inputs.
        /// </summary>
        public static EdgeReadResult EdgeRead(List<BallastTrade> trades, int minSample)
        {
            EdgeReadResult r = new EdgeReadResult();
            if (trades == null) trades = new List<BallastTrade>();

            int n = trades.Count;
            r.Count = n;

            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double net = trades[i].Pnl - (trades[i].Commission > 0 ? trades[i].Commission : 0);
                sum += net;
                sumSq += net * net;
                if (net > 0) r.Wins++;
            }

            r.Total = sum;
            r.WinRate = n > 0 ? (double)r.Wins / n : 0;
            r.Expectancy = n > 0 ? sum / n : 0;

            if (n > 1)
            {
                double variance = (sumSq - n * r.Expectancy * r.Expectancy) / (n - 1);
                if (variance < 0) variance = 0;              // floating-point guard
                double sd = Math.Sqrt(variance);
                r.TStat = sd > 0 ? r.Expectancy / (sd / Math.Sqrt(n)) : 0;
            }

            if (minSample < 2) minSample = 2;

            string exp = BallastTrade.Money(r.Expectancy) + " a trade";
            string t = "t=" + r.TStat.ToString("0.0", CultureInfo.InvariantCulture);

            if (n < minSample)
            {
                r.Confidence = EdgeConfidence.TooFew;
                r.Verdict = "Not enough trades yet (" + n + " of " + minSample
                          + "). No verdict until the sample is there — keep going.";
            }
            else if (r.Expectancy <= 0)
            {
                r.Confidence = EdgeConfidence.NoEdge;
                r.Verdict = "Expectancy is " + exp + " after costs. This setup is losing money, not making it.";
            }
            else if (r.TStat < 1.7)
            {
                r.Confidence = EdgeConfidence.InTheNoise;
                r.Verdict = "Positive (" + exp + ") but inside the noise (" + t
                          + "). This could easily be luck — more trades, or it is not real.";
            }
            else if (r.TStat < 2.5)
            {
                r.Confidence = EdgeConfidence.ProbablyReal;
                r.Verdict = exp + ", and probably real (" + t
                          + "). Promising — do not touch it, finish the sample.";
            }
            else
            {
                r.Confidence = EdgeConfidence.LikelyReal;
                r.Verdict = exp + ", and unlikely to be luck (" + t
                          + "). This looks like a genuine edge — prove it out, then size up slowly.";
            }

            return r;
        }

        /// <summary>
        /// EdgeRead for one setup key over manual trades only. The convenience the
        /// window actually calls, one per setup on the Journal tab.
        /// </summary>
        public EdgeReadResult EdgeForSetup(List<BallastTrade> source, string setupKey, int minSample)
        {
            List<BallastTrade> manual = ManualOnly(source);
            List<BallastTrade> forSetup = new List<BallastTrade>();
            for (int i = 0; i < manual.Count; i++)
                if (string.Equals(manual[i].Setup, setupKey, StringComparison.Ordinal))
                    forSetup.Add(manual[i]);
            return EdgeRead(forSetup, minSample);
        }

        /// <summary>
        /// Trades opened while Ballast was advising against it, versus the rest.
        /// Needs no tagging at all — it is entirely machine-observed, so it is
        /// the one insight that survives a trader who never touches a tag button.
        /// </summary>
        public List<JournalBucket> AdviceSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);
            JournalBucket against = new JournalBucket(); against.Label = "Taken after Ballast said stop";
            JournalBucket with = new JournalBucket(); with.Label = "Taken with a clear signal";

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].TakenAgainstAdvice) against.Add(source[i]);
                else with.Add(source[i]);
            }

            List<JournalBucket> list = new List<JournalBucket>();
            list.Add(against); list.Add(with);
            return list;
        }

        /// <summary>
        /// Trades where the stop or target was moved once the trade was on,
        /// against trades that were left alone.
        ///
        /// Only trades the trader actually answered the question on are counted.
        /// A blank means "not said", and rolling unanswered trades into "held"
        /// would flatter the numbers - which is the one thing a journal must
        /// never do, because the whole point of it is to be believed.
        /// </summary>
        public List<JournalBucket> MovedSplit(List<BallastTrade> source)
        {
            source = ManualOnly(source);
            JournalBucket moved = new JournalBucket(); moved.Label = "Moved the stop or target";
            JournalBucket held = new JournalBucket(); held.Label = "Left them where they were";

            for (int i = 0; i < source.Count; i++)
            {
                string m = source[i].Moved;
                if (m == null || m.Length == 0) continue;      // not answered
                if (DidMove(m)) moved.Add(source[i]);
                else held.Add(source[i]);
            }

            List<JournalBucket> list = new List<JournalBucket>();
            list.Add(moved); list.Add(held);
            return list;
        }

        /// <summary>
        /// Trades entered within <paramref name="windowMinutes"/> of a losing
        /// trade — the revenge window — against everything else.
        /// </summary>
        public List<JournalBucket> RevengeSplit(List<BallastTrade> source, int windowMinutes)
        {
            source = ManualOnly(source);
            JournalBucket soon = new JournalBucket();
            soon.Label = "Within " + windowMinutes + " min of a loss";
            JournalBucket rest = new JournalBucket(); rest.Label = "Everything else";

            for (int i = 0; i < source.Count; i++)
            {
                BallastTrade e = source[i];
                bool inWindow = e.PreviousTradeWasLoss
                             && e.MinutesSincePreviousLoss >= 0
                             && e.MinutesSincePreviousLoss < windowMinutes;
                if (inWindow) soon.Add(e); else rest.Add(e);
            }

            List<JournalBucket> list = new List<JournalBucket>();
            list.Add(soon); list.Add(rest);
            return list;
        }

        /// <summary>
        /// Money made and lost per instrument, worst first.
        ///
        /// Traders usually have one instrument that quietly funds the others'
        /// losses, and one that bleeds. It is invisible in a total and obvious
        /// the moment it is split out.
        /// </summary>
        public List<JournalBucket> InstrumentSplit(List<BallastTrade> source)
        {
            Dictionary<string, JournalBucket> map = new Dictionary<string, JournalBucket>();
            List<JournalBucket> list = new List<JournalBucket>();
            if (source == null) return list;

            for (int i = 0; i < source.Count; i++)
            {
                string key = InstrumentRoot(source[i].Instrument);
                if (key.Length == 0) key = "unknown";

                JournalBucket b;
                if (!map.TryGetValue(key, out b))
                {
                    b = new JournalBucket();
                    b.Label = key;
                    map[key] = b;
                    list.Add(b);
                }
                b.Add(source[i]);
            }

            // Worst first: the leak matters more than the winner.
            list.Sort(delegate(JournalBucket a, JournalBucket c) { return a.Net.CompareTo(c.Net); });
            return list;
        }

        /// <summary>
        /// "ES 09-26" and "ES 12-26" are the same instrument to a trader, so they
        /// are pooled. Splitting by contract month would scatter one instrument's
        /// record across every quarterly roll.
        /// </summary>
        public static string InstrumentRoot(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return "";
            string t = instrument.Trim().ToUpperInvariant();

            int cut = t.Length;
            int sp = t.IndexOf(' ');
            if (sp >= 0 && sp < cut) cut = sp;
            return t.Substring(0, cut);
        }

        /// <summary>
        /// Net P&L of the first N trades of each day against everything after,
        /// which is how "I was up and gave it all back" shows up in numbers.
        /// </summary>
        public List<JournalBucket> GiveBackSplit(List<BallastTrade> source, int firstN)
        {
            source = ManualOnly(source);
            JournalBucket early = new JournalBucket(); early.Label = "First " + firstN + " trades of the day";
            JournalBucket late = new JournalBucket(); late.Label = "Trades after that";

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].TradeNumberToday <= firstN) early.Add(source[i]);
                else late.Add(source[i]);
            }

            List<JournalBucket> list = new List<JournalBucket>();
            list.Add(early); list.Add(late);
            return list;
        }

        /// <summary>
        /// The one sentence worth putting in front of the trader. Picks whichever
        /// split shows the largest, best-evidenced gap, and says nothing at all
        /// when the sample is too thin to mean anything — a journal that invents
        /// patterns from four trades is worse than no journal.
        /// </summary>
        public string HeadlineInsight(List<BallastTrade> source, int cooldownMinutes, int minSample)
        {
            // Judged on your own trades only. A bot that took 400 trades does not
            // mean you have earned a conclusion about your discipline.
            source = ManualOnly(source);

            if (source == null || source.Count < minSample)
            {
                int have = source == null ? 0 : source.Count;
                return "Not enough trades yet - " + have + " of " + minSample
                     + " before Ballast will draw any conclusion.";
            }

            string best = null;
            double bestGap = 0;

            // Each candidate: a label pair and the cost of the worse side.
            List<JournalBucket> advice = AdviceSplit(source);
            if (advice[0].Count >= 3 && advice[0].Net < 0)
            {
                double gap = -advice[0].Net;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = advice[0].Count + " trades were opened after Ballast said stop, and together they cost "
                         + BallastTrade.Money(-advice[0].Net) + ". The rest made "
                         + BallastTrade.Money(advice[1].Net) + ".";
                }
            }

            // The planned/unplanned aggregate - but worded with the SPECIFIC
            // verdict when one kind explains most of it. Same underlying loss,
            // better sentence: "the four you chased cost $1,600" names something
            // to stop doing, "your unplanned trades lost $1,900" does not.
            //
            // Note it does NOT outrank the against-advice figure above on a tie.
            // That one is machine-observed and survives a trader who never tags
            // honestly; this one is self-reported. When both explain the same
            // money, the evidence that cannot be flattered wins.
            List<JournalBucket> planned = PlannedSplit(source);
            if (planned[1].Count >= 3 && planned[1].Net < 0)
            {
                double gap = -planned[1].Net;

                string text = "Your " + planned[1].Count + " unplanned trades lost "
                            + BallastTrade.Money(-planned[1].Net) + ". Your planned trades made "
                            + BallastTrade.Money(planned[0].Net) + ".";

                JournalBucket worstVerdict = null;
                List<JournalBucket> verdicts = VerdictSplit(source);
                for (int v = 0; v < verdicts.Count; v++)
                {
                    JournalBucket b = verdicts[v];
                    if (b.Count < 3 || b.Net >= 0) continue;
                    if (b.Label == VerdictLabel(Verdict_ByTheBook)) continue;   // the goal, not a leak
                    if (worstVerdict == null || b.Net < worstVerdict.Net) worstVerdict = b;
                }

                if (worstVerdict != null && gap > 0 && (-worstVerdict.Net) / gap >= 0.5)
                {
                    text = worstVerdict.Count + " trades you tagged \"" + worstVerdict.Label
                         + "\" cost you " + BallastTrade.Money(-worstVerdict.Net) + ".";
                }

                if (gap > bestGap) { bestGap = gap; best = text; }
            }

            List<JournalBucket> revenge = RevengeSplit(source, cooldownMinutes);
            if (revenge[0].Count >= 3 && revenge[0].Net < 0)
            {
                double gap = -revenge[0].Net;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = revenge[0].Count + " trades were opened within " + cooldownMinutes
                         + " minutes of a loss, costing " + BallastTrade.Money(-revenge[0].Net) + ".";
                }
            }

            // Moving a stop is the most expensive habit in trading and the only
            // one Ballast cannot observe for itself, so when the trader has
            // answered enough times for it to mean something, it competes on
            // equal terms with everything else.
            List<JournalBucket> moved = MovedSplit(source);
            if (moved[0].Count >= 3 && moved[0].Net < 0)
            {
                double gap = -moved[0].Net;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = "The " + moved[0].Count + " trades where you moved your stop or target cost you "
                         + BallastTrade.Money(-moved[0].Net) + ".";
                    if (moved[1].Count >= 3)
                        best += " The " + moved[1].Count + " you left alone made "
                              + BallastTrade.Money(moved[1].Net) + ".";
                }
            }

            if (best == null)
                return "Nothing is standing out as a leak yet. Keep tagging - the comparisons get sharper with more trades.";

            return best;
        }

        // ── Persistence ──────────────────────────────────────────────────────
        // CSV so the trader can open it in Excel and do whatever they like with
        // it. Their journal is their property; it must never be locked inside
        // this tool's own format.

        public const string CsvHeader =
            "Account,Instrument,Direction,Contracts,EntryTime,ExitTime,DurationMin,PnL," +
            "TradeNoToday,DailyPnLBefore,CushionAtEntry,FloorAtEntry,MinSincePrevLoss," +
            "PrevWasLoss,InWindow,AdviceAtEntry,Planned,Feeling,SessionPlan,Note," +
            "EntryImage,ExitImage,Done,Automated,Moved,Commission,Setup";

        private static string Esc(string s)
        {
            if (s == null) return "";
            bool needs = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
                      || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
            if (!needs) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string N(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string T(DateTime d)
        {
            return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static string ToCsvLine(BallastTrade e)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Esc(e.AccountName)).Append(',');
            sb.Append(Esc(e.Instrument)).Append(',');
            sb.Append(e.DirectionLabel).Append(',');
            sb.Append(e.MaxContracts.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(T(e.EntryTime)).Append(',');
            sb.Append(T(e.ExitTime)).Append(',');
            sb.Append(N(e.DurationMinutes)).Append(',');
            sb.Append(N(e.Pnl)).Append(',');
            sb.Append(e.TradeNumberToday.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(N(e.DailyPnlBefore)).Append(',');
            sb.Append(N(e.CushionAtEntry)).Append(',');
            sb.Append(N(e.FloorAtEntry)).Append(',');
            sb.Append(e.MinutesSincePreviousLoss.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(e.PreviousTradeWasLoss ? "1" : "0").Append(',');
            sb.Append(e.InsideSessionWindow ? "1" : "0").Append(',');
            sb.Append(Esc(e.AdviceAtEntry)).Append(',');
            sb.Append(Esc(e.Planned)).Append(',');
            sb.Append(Esc(e.Feeling)).Append(',');
            sb.Append(Esc(e.SessionPlan)).Append(',');
            sb.Append(Esc(e.Note)).Append(',');
            sb.Append(Esc(e.EntryImage)).Append(',');
            sb.Append(Esc(e.ExitImage)).Append(',');
            sb.Append(e.Dismissed ? "1" : "0").Append(',');
            sb.Append(e.Automated ? "1" : "0").Append(',');
            sb.Append(Esc(e.Moved)).Append(',');
            sb.Append(N(e.Commission)).Append(',');
            sb.Append(Esc(e.Setup));
            return sb.ToString();
        }

        /// <summary>Split one CSV line, honouring quoted fields.</summary>
        public static List<string> SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            if (line == null) return fields;

            StringBuilder cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            fields.Add(cur.ToString());
            return fields;
        }

        public static BallastTrade FromCsvLine(string line)
        {
            List<string> f = SplitCsvLine(line);
            if (f.Count < 20) return null;

            BallastTrade e = new BallastTrade();
            e.AccountName = f[0];
            e.Instrument = f[1];
            e.IsLong = f[2] == "Long";

            int iv; double dv; DateTime tv;
            if (int.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out iv)) e.MaxContracts = iv;
            if (DateTime.TryParse(f[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out tv)) e.EntryTime = tv;
            if (DateTime.TryParse(f[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out tv)) e.ExitTime = tv;
            // f[6] duration is derived; recomputed from the timestamps on read.
            if (double.TryParse(f[7], NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) e.Pnl = dv;
            if (int.TryParse(f[8], NumberStyles.Any, CultureInfo.InvariantCulture, out iv)) e.TradeNumberToday = iv;
            if (double.TryParse(f[9], NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) e.DailyPnlBefore = dv;
            if (double.TryParse(f[10], NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) e.CushionAtEntry = dv;
            if (double.TryParse(f[11], NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) e.FloorAtEntry = dv;
            if (int.TryParse(f[12], NumberStyles.Any, CultureInfo.InvariantCulture, out iv)) e.MinutesSincePreviousLoss = iv;
            e.PreviousTradeWasLoss = f[13] == "1";
            e.InsideSessionWindow = f[14] == "1";
            e.AdviceAtEntry = f[15];
            e.Planned = f[16];
            e.Feeling = f[17];
            e.SessionPlan = f[18];
            e.Note = f[19];

            // Images arrived after the first journal format. Older rows simply
            // have no pictures rather than failing to load.
            if (f.Count > 20) e.EntryImage = f[20];
            if (f.Count > 21) e.ExitImage = f[21];
            if (f.Count > 22) e.Dismissed = f[22] == "1";
            // Rows written before Dismissed existed are already tagged and gone;
            // treat them as cleared so they do not all reappear on upgrade.
            else if (f.Count > 20) e.Dismissed = e.IsTagged;
            if (f.Count > 23) e.Automated = f[23] == "1";

            // Whether the stop or target was moved. Rows written before the
            // question existed leave it blank, which reads as "not said" rather
            // than as "held" - a journal must never answer on the trader's behalf.
            if (f.Count > 24) e.Moved = f[24];

            // Commission arrived last. An older row simply does not know what it
            // cost, which is different from having cost nothing - so it reports 0
            // and is left out of any figure that would be wrong without it.
            if (f.Count > 25 && double.TryParse(f[25], NumberStyles.Any,
                                                CultureInfo.InvariantCulture, out dv))
                e.Commission = dv;

            // Setup arrived last of all. A row written before the field existed
            // simply has no setup label, which reads as "" - not tagged - rather
            // than as a wrong one, so it is left out of the per-setup split
            // instead of being pooled into the wrong strategy.
            if (f.Count > 26) e.Setup = f[26];

            return e;
        }

        /// <summary>
        /// Rewrite the whole file. Called after a tag changes, which is rare and
        /// cheap at journal scale (a few thousand lines a year), and avoids the
        /// bug class where an appended correction disagrees with the original.
        /// </summary>
        public bool Save(string path)
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add(CsvHeader);
                for (int i = 0; i < entries.Count; i++) lines.Add(ToCsvLine(entries[i]));
                AtomicFile.WriteAllLines(path, lines.ToArray());
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
                entries.Clear();

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null || line.Length == 0) continue;
                    if (i == 0 && line.StartsWith("Account,")) continue;

                    BallastTrade e = FromCsvLine(line);
                    if (e != null) entries.Add(e);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
