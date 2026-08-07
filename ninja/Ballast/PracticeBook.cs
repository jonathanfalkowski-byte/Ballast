// ─────────────────────────────────────────────────────────────────────────────
// Practice.
//
// "i ws practicing on the playtest connection, i was thinking i would like to
// use ballast as well on that account but it is all about testing things and
// practicing is there something we can add to that...do we even want to record
// that or is there something else we can do ...to help the trader realize his
// mistakes or patterns?"
//
// Two answers, and the first one is a warning rather than a feature.
//
// Recording replay trades in the ordinary journal would quietly wreck it.
// Everything Ballast stamps comes from Core.Globals.Now, and on a Playback
// connection that is the REPLAY clock - so a trade taken while replaying the
// sixth of August is written into the journal dated the sixth of August, in
// among the real trades taken that morning on a funded account. Setup edges,
// the time-of-day tail, the pressure profile and "does setup B work" would all
// absorb them. And because a replay can be rewound, running the same morning
// three times files the same trade three times as three separate pieces of
// evidence. The journal's whole authority is that every row in it is something
// that actually happened, once, at the time it says.
//
// So practice is kept in its own book, and it is scored on something else.
//
// Money is not information here. The fills are modelled, the clock is invented
// and the session can be replayed until it works. What IS real is behaviour:
// whether the cooldown was waited out, whether the count was kept, whether a
// trade was taken after Ballast had already said stop. Those are the same
// mistakes that cost money live, and they are free to make here.
//
// And replay can do one thing nothing else in Ballast can. The sim-versus-real
// split is the only experiment a trader runs on himself - same person, same
// setups, one thing changed. Replay is the cleaner version, because even the
// market is held still: replay the same morning twice and the bars, the news
// and the chop are identical, so the ONLY variable is him. That makes a
// second run a controlled test of a rule rather than another anecdote.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballast
{
    /// <summary>
    /// One pass through one replayed session. A rewind starts a new one.
    /// </summary>
    public class PracticeRun
    {
        /// <summary>The day being replayed - not the day he sat down to replay it.</summary>
        public DateTime SessionDate;

        /// <summary>1 for the first time through this session, 2 for the next, and so on.</summary>
        public int RunNumber;

        /// <summary>Real wall-clock time he started this pass. Orders runs of the same day.</summary>
        public DateTime StartedAt;

        public string AccountName = "";

        public readonly List<BallastTrade> Trades = new List<BallastTrade>();
    }

    /// <summary>
    /// What a pass looked like. Counts of things done, never dollars.
    /// </summary>
    public class PracticeScore
    {
        public int Trades;

        /// <summary>Trades taken beyond the count he set for the account.</summary>
        public int PastTheCount;

        /// <summary>Trades opened inside his own cooldown after a loss.</summary>
        public int InsideCooldown;

        /// <summary>Trades opened after Ballast had already said stop, cool off or protect.</summary>
        public int AfterAStopSignal;

        /// <summary>Trades he himself tagged as not the plan.</summary>
        public int OffPlan;

        /// <summary>Trades that broke none of the above.</summary>
        public int Clean;

        /// <summary>
        /// Clean trades as a share of trades taken, 0 to 1. -1 when there were no
        /// trades, which is not a score of zero - it is the absence of one.
        /// </summary>
        public double Adherence
        {
            get { return Trades <= 0 ? -1 : (double)Clean / Trades; }
        }
    }

    public class PracticeBook
    {
        private readonly List<PracticeRun> runs = new List<PracticeRun>();

        public int Count { get { return runs.Count; } }
        public List<PracticeRun> All { get { return new List<PracticeRun>(runs); } }

        /// <summary>
        /// The run a trade at this replay time belongs to, creating one if this
        /// is a session - or a pass - that has not been seen before.
        ///
        /// A rewind is what starts a new pass, and a rewind is visible: the
        /// replay clock goes BACKWARDS. Nothing else in a session ever does
        /// that, which makes it a reliable signal and one that costs nothing to
        /// watch. Loading a different day is the same event seen from further
        /// away - the date changes - and both mean the same thing: whatever
        /// happens next is a fresh attempt at the same market.
        /// </summary>
        public PracticeRun RunFor(string account, DateTime replayNow, DateTime realNow)
        {
            PracticeRun current = Latest(account);

            if (current != null
                && current.SessionDate == replayNow.Date
                && !WentBackwards(current, replayNow))
                return current;

            PracticeRun next = new PracticeRun();
            next.AccountName = account ?? "";
            next.SessionDate = replayNow.Date;
            next.StartedAt = realNow;
            next.RunNumber = RunsOf(account, replayNow.Date) + 1;
            runs.Add(next);
            return next;
        }

        /// <summary>
        /// How far back the replay clock has to go before it counts as starting
        /// again rather than scrubbing.
        ///
        /// The two ways to get this wrong are not equally bad. Too eager and one
        /// pass is split into three, so a session he traded badly reads as three
        /// short tidy ones. Too lax and two attempts merge, so twelve trades
        /// look like one long undisciplined morning. The first is worse, because
        /// it flatters him.
        ///
        /// A genuine restart in Market Replay goes back to the session open -
        /// hours, not minutes. Stepping back to re-watch a move is seconds. Five
        /// minutes sits in the empty space between those and is nowhere near
        /// either.
        /// </summary>
        private const double RewindMinutes = 5.0;

        private static bool WentBackwards(PracticeRun run, DateTime replayNow)
        {
            if (run.Trades.Count == 0) return false;

            // Against the LAST trade rather than the first: a replay stepped back
            // and resumed is still the same pass until it goes back well before
            // the last thing that happened in it.
            DateTime last = run.Trades[run.Trades.Count - 1].ExitTime;
            return replayNow < last.AddMinutes(-RewindMinutes);
        }

        public int RunsOf(string account, DateTime sessionDate)
        {
            int n = 0;
            for (int i = 0; i < runs.Count; i++)
                if (Same(runs[i].AccountName, account) && runs[i].SessionDate == sessionDate.Date) n++;
            return n;
        }

        public PracticeRun Latest(string account)
        {
            for (int i = runs.Count - 1; i >= 0; i--)
                if (Same(runs[i].AccountName, account)) return runs[i];
            return null;
        }

        /// <summary>Every pass at one session, in the order they were made.</summary>
        public List<PracticeRun> RunsFor(string account, DateTime sessionDate)
        {
            List<PracticeRun> list = new List<PracticeRun>();
            for (int i = 0; i < runs.Count; i++)
                if (Same(runs[i].AccountName, account) && runs[i].SessionDate == sessionDate.Date)
                    list.Add(runs[i]);
            return list;
        }

        public void Add(PracticeRun run) { if (run != null) runs.Add(run); }
        public void Clear() { runs.Clear(); }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        // ── Scoring ─────────────────────────────────────────────────────────

        /// <summary>
        /// What this pass looked like, in behaviour.
        ///
        /// Bot trades and reconstructed rows are left out for the same reason
        /// they are left out of the journal: a strategy has no discipline to
        /// report on, and a gap has no detail to report.
        /// </summary>
        public static PracticeScore Score(PracticeRun run, int maxTrades, int cooldownMinutes)
        {
            PracticeScore s = new PracticeScore();
            if (run == null) return s;

            for (int i = 0; i < run.Trades.Count; i++)
            {
                BallastTrade e = run.Trades[i];
                if (e == null || e.Automated || e.IsReconstructed) continue;

                s.Trades++;

                bool broke = false;

                if (maxTrades > 0 && e.TradeNumberToday > maxTrades) { s.PastTheCount++; broke = true; }

                if (cooldownMinutes > 0 && e.PreviousTradeWasLoss
                    && e.MinutesSincePreviousLoss >= 0
                    && e.MinutesSincePreviousLoss < cooldownMinutes)
                { s.InsideCooldown++; broke = true; }

                if (e.TakenAgainstAdvice) { s.AfterAStopSignal++; broke = true; }

                // Only counted where he actually answered. A blank is "not said",
                // and treating silence as a clean trade would flatter the score -
                // which is the one thing a practice measure must never do, since
                // the entire point of it is to be believed when it says he
                // improved.
                if (e.Planned == BallastJournal.Verdict_Chased
                    || e.Planned == BallastJournal.Verdict_OffPlan)
                { s.OffPlan++; broke = true; }

                if (!broke) s.Clean++;
            }

            return s;
        }

        // ── The comparison ──────────────────────────────────────────────────

        /// <summary>
        /// Two passes at the same session, read against each other.
        ///
        /// This is the sentence the whole file exists for. Same bars, same news,
        /// same chop - the market is held still, so anything that moved between
        /// these two numbers is him. Nothing else Ballast measures can say that:
        /// live, a better week might just be a better week.
        ///
        /// Returns "" when there is nothing honest to say - a pass with no trades
        /// in it, or a comparison against a session that was only ever run once.
        /// </summary>
        public static string Compare(PracticeRun before, PracticeRun after,
                                     int maxTrades, int cooldownMinutes)
        {
            if (before == null || after == null) return "";
            if (before.SessionDate != after.SessionDate) return "";

            PracticeScore a = Score(before, maxTrades, cooldownMinutes);
            PracticeScore b = Score(after, maxTrades, cooldownMinutes);
            if (a.Trades == 0 || b.Trades == 0) return "";

            string day = after.SessionDate.ToString("d MMM", CultureInfo.InvariantCulture);

            string s = "Same session - " + day + ", run " + before.RunNumber
                     + " against run " + after.RunNumber + ". "
                     + "The bars were identical both times, so anything that changed here is you.  "
                     + "Trades " + a.Trades + " to " + b.Trades + ". "
                     + "Kept to your rules " + a.Clean + " of " + a.Trades
                     + ", then " + b.Clean + " of " + b.Trades + ".";

            string moved = Moved("past your count", a.PastTheCount, b.PastTheCount)
                         + Moved("inside your cooldown", a.InsideCooldown, b.InsideCooldown)
                         + Moved("after Ballast said stop", a.AfterAStopSignal, b.AfterAStopSignal)
                         + Moved("off your plan", a.OffPlan, b.OffPlan);

            if (moved.Length > 0) s += moved;

            // The honest reading of the whole thing, and it refuses to call a
            // one-trade difference progress.
            if (b.Clean == b.Trades && a.Clean < a.Trades)
                s += "  You traded that morning by your own rules the second time.";
            else if (b.Adherence > a.Adherence + 0.15)
                s += "  Better - and on the same market, which is the only way to know it was better.";
            else if (b.Adherence < a.Adherence - 0.15)
                s += "  Worse, on a morning you had already seen once.";
            else
                s += "  No real change. The same session twice is where a rule gets tested, "
                   + "so pick ONE thing to do differently and run it again.";

            return s;
        }

        private static string Moved(string what, int was, int now)
        {
            if (was == now) return "";
            return "  " + what.Substring(0, 1).ToUpperInvariant() + what.Substring(1)
                 + ": " + was + " to " + now + ".";
        }

        // ── The file ────────────────────────────────────────────────────────
        //
        // Its own file, so that nothing here can ever be read by the code that
        // answers questions about real trading.

        public List<string> Serialise()
        {
            List<string> lines = new List<string>();
            lines.Add("SessionDate,RunNumber,StartedAt,Account,TradeNo,EntryTime,ExitTime,"
                    + "Instrument,Contracts,MinSincePrevLoss,PrevWasLoss,AdviceAtEntry,Planned,Setup");

            for (int i = 0; i < runs.Count; i++)
            {
                PracticeRun r = runs[i];
                for (int k = 0; k < r.Trades.Count; k++)
                {
                    BallastTrade e = r.Trades[k];
                    if (e == null) continue;

                    lines.Add(string.Join(",", new string[] {
                        r.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        r.RunNumber.ToString(CultureInfo.InvariantCulture),
                        r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Csv(r.AccountName),
                        e.TradeNumberToday.ToString(CultureInfo.InvariantCulture),
                        e.EntryTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        e.ExitTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Csv(e.Instrument),
                        e.MaxContracts.ToString(CultureInfo.InvariantCulture),
                        e.MinutesSincePreviousLoss.ToString(CultureInfo.InvariantCulture),
                        e.PreviousTradeWasLoss ? "1" : "0",
                        Csv(e.AdviceAtEntry),
                        Csv(e.Planned),
                        Csv(e.Setup)
                    }));
                }
            }

            return lines;
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
