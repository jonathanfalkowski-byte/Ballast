using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "i treat my sim differently than a real account or eval account....so there
/// is some analysis there... you may want to go into the whole psychology thing"
///
/// The sim/funded gap is the only controlled experiment a trader ever runs on
/// himself. Same person, same setups, same market, same hours, and one variable
/// moved: whether the money is real. Everything a backtest cannot tell him lives
/// in that difference.
///
/// The comparison is of BEHAVIOUR and never of money, and that is not
/// squeamishness. A simulator's fills flatter you - no slippage, no queue,
/// limits that fill when they would not have - so a P&L gap is part psychology
/// and part generosity and the two cannot be separated. But no fill engine
/// decides whether a trade was chased, whether a winner was held to target, or
/// how many seconds after a loss the next click came. Those are the person.
///
/// These tests are mostly about SILENCE. The worst outcome here is not saying
/// nothing; it is telling a trader something about himself that is not true.
/// </summary>
public static class PressureTests
{
    public static void Run()
    {
        ItSaysNothingOnAThinSample();
        ChasingUnderPressureIsNamed();
        CuttingWinnersAndNursingLosersIsNamed();
        DoingBetterUnderPressureIsAlsoNamed();
        NoDifferenceMeansNoSentence();
        MoneyIsNeverTheArgument();
        BotTradesAndGapsAreNotBehaviour();
        PeriodsAreCalendarPeriods();
        EntriesAreJudgedWorstFirst();
    }

    /// <summary>
    /// "look at your week, month year to date"
    ///
    /// Calendar periods, because that is how a trader talks. "This week" ends on
    /// Sunday whatever Monday looked like, and "year to date" is January onward
    /// rather than the last 365 days.
    ///
    /// A trade belongs to the day it was CLOSED. That is the day its result
    /// landed on the account and the day he lived through it - a position opened
    /// on Friday and let go on Monday is a Monday problem.
    /// </summary>
    static void PeriodsAreCalendarPeriods()
    {
        T.S("week, month and year are calendar periods");

        // Thursday 6 August 2026.
        DateTime now = new DateTime(2026, 8, 6, 14, 0, 0);

        T.Eq(BallastJournal.PeriodStart(now, JournalPeriod.Today),
             new DateTime(2026, 8, 6), "today starts at midnight");
        T.Eq(BallastJournal.PeriodStart(now, JournalPeriod.Week),
             new DateTime(2026, 8, 3), "the week starts on Monday");
        T.Eq(BallastJournal.PeriodStart(now, JournalPeriod.Month),
             new DateTime(2026, 8, 1), "the month on the first");
        T.Eq(BallastJournal.PeriodStart(now, JournalPeriod.Year),
             new DateTime(2026, 1, 1), "and year to date means January");

        // A Sunday counts back to the Monday that led into it, not forward.
        DateTime sunday = new DateTime(2026, 8, 9, 20, 0, 0);
        T.Eq(BallastJournal.PeriodStart(sunday, JournalPeriod.Week),
             new DateTime(2026, 8, 3), "Sunday belongs to the week it trails, not the one it leads");

        List<BallastTrade> book = new List<BallastTrade>();
        BallastTrade july = Tr(0, 100, 10, BallastJournal.Verdict_ByTheBook, -1, 1);
        july.EntryTime = new DateTime(2026, 7, 30, 10, 0, 0);
        july.ExitTime = july.EntryTime.AddMinutes(10);
        book.Add(july);

        BallastTrade monday = Tr(0, 100, 10, BallastJournal.Verdict_ByTheBook, -1, 1);
        monday.EntryTime = new DateTime(2026, 8, 3, 10, 0, 0);
        monday.ExitTime = monday.EntryTime.AddMinutes(10);
        book.Add(monday);

        // Opened last week, closed this one. It is a this-week trade.
        BallastTrade across = Tr(0, 100, 10, BallastJournal.Verdict_ByTheBook, -1, 1);
        across.EntryTime = new DateTime(2026, 7, 31, 15, 0, 0);
        across.ExitTime = new DateTime(2026, 8, 4, 9, 0, 0);
        book.Add(across);

        T.Eq(BallastJournal.InPeriod(book, now, JournalPeriod.Week).Count, 2,
             "the Monday trade and the one that closed on Tuesday");
        T.Eq(BallastJournal.InPeriod(book, now, JournalPeriod.Month).Count, 2,
             "August by close, so July's opening does not drag it back");
        T.Eq(BallastJournal.InPeriod(book, now, JournalPeriod.Year).Count, 3, "all three this year");
        T.Eq(BallastJournal.InPeriod(book, now, JournalPeriod.Everything).Count, 3, "and all of them ever");
    }

    /// <summary>
    /// "i want to know if my entry strategies work...i think that is important
    /// too, no?"
    ///
    /// It is the most important thing, and the one question a trader can never
    /// answer from memory: the setup that FEELS like it works is the one whose
    /// wins are memorable, which has nothing to do with whether it makes money.
    /// </summary>
    static void EntriesAreJudgedWorstFirst()
    {
        T.S("entry strategies, worst money first");

        BallastJournal j = new BallastJournal();
        List<BallastTrade> book = new List<BallastTrade>();

        for (int i = 0; i < 10; i++)
        {
            BallastTrade good = Tr(i % 3, 200, 20, BallastJournal.Verdict_ByTheBook, -1, 1);
            good.Setup = "A - EMA cross";
            book.Add(good);

            BallastTrade bad = Tr(i % 3, -150, 20, BallastJournal.Verdict_ByTheBook, -1, 1);
            bad.Setup = "B - pivot";
            book.Add(bad);
        }

        List<EdgeReadResult> edges = j.SetupEdges(book, 20);
        T.Eq(edges.Count, 2, "both setups reported");
        T.Ok(edges[0].Verdict.IndexOf("B - pivot") >= 0,
             "the one costing money is first: " + edges[0].Verdict);
        T.Near(edges[0].Total, -1500, 0.01, "with what it cost");
        T.Near(edges[1].Total, 2000, 0.01, "and the other with what it made");

        // A setup he has since retired still has to report what it did, or
        // deleting a bad setup would delete the evidence that it was bad.
        BallastJournal.Setups = new List<string>();
        T.Eq(j.SetupEdges(book, 20).Count, 2,
             "the labels on the trades are the record, not the current list");
    }


    static readonly DateTime D0 = new DateTime(2026, 8, 3, 10, 0, 0);

    /// <summary>One trade. Hold time in minutes, and what it was tagged.</summary>
    static BallastTrade Tr(int day, double pnl, double minutes, string planned,
                           int minsSinceLoss, int contracts)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "acct";
        e.Instrument = "NQ SEP26";
        e.MaxContracts = contracts;
        e.EntryTime = D0.AddDays(day);
        e.ExitTime = e.EntryTime.AddMinutes(minutes);
        e.Pnl = pnl;
        e.Planned = planned;
        e.MinutesSincePreviousLoss = minsSinceLoss;
        e.PreviousTradeWasLoss = minsSinceLoss >= 0;
        return e;
    }

    /// <summary>n trades, all alike, spread over three days.</summary>
    static List<BallastTrade> Book(int n, double pnl, double minutes, string planned,
                                   int minsSinceLoss, int contracts)
    {
        List<BallastTrade> list = new List<BallastTrade>();
        for (int i = 0; i < n; i++)
            list.Add(Tr(i % 3, pnl, minutes, planned, minsSinceLoss, contracts));
        return list;
    }

    static BehaviourProfile P(List<BallastTrade> t, string label)
    {
        return BallastJournal.Behaviour(t, label, 5);
    }

    static void ItSaysNothingOnAThinSample()
    {
        T.S("a thin sample says nothing at all");

        // A gap so wide it would shout, on far too few trades to mean it.
        List<BallastTrade> sim = Book(4, 100, 20, BallastJournal.Verdict_ByTheBook, -1, 1);
        List<BallastTrade> real = Book(4, -100, 2, BallastJournal.Verdict_Chased, 1, 1);

        List<string> lines = BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded"));
        T.Eq(lines.Count, 0, "four trades a side proves nothing about a person");

        // And one side being thin is enough to disqualify the pair.
        List<BallastTrade> plenty = Book(20, 100, 20, BallastJournal.Verdict_ByTheBook, -1, 1);
        T.Eq(BallastJournal.PressureGap(P(plenty, "practice"), P(real, "funded")).Count, 0,
             "a big control against a small test is still not a comparison");
    }

    static void ChasingUnderPressureIsNamed()
    {
        T.S("rules going first when it is real");

        // Practice: all by the book. Funded: two thirds taken off plan.
        List<BallastTrade> sim = Book(12, 50, 10, BallastJournal.Verdict_ByTheBook, -1, 1);

        List<BallastTrade> real = new List<BallastTrade>();
        real.AddRange(Book(8, -50, 10, BallastJournal.Verdict_Chased, -1, 1));
        real.AddRange(Book(4, 50, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<string> lines = BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded"));

        bool found = false;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].IndexOf("off plan") >= 0) found = true;

        T.Ok(found, "the difference is stated: " + string.Join(" / ", lines.ToArray()));
    }

    static void CuttingWinnersAndNursingLosersIsNamed()
    {
        T.S("cutting winners and sitting with losers when it counts");

        // Practice: winners held 30 minutes, losers 10 - three to one.
        List<BallastTrade> sim = new List<BallastTrade>();
        sim.AddRange(Book(6, 100, 30, BallastJournal.Verdict_ByTheBook, -1, 1));
        sim.AddRange(Book(6, -100, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        // Funded: the same trader, winners grabbed at 5, losers nursed for 40.
        List<BallastTrade> real = new List<BallastTrade>();
        real.AddRange(Book(6, 100, 5, BallastJournal.Verdict_ByTheBook, -1, 1));
        real.AddRange(Book(6, -100, 40, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<string> lines = BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded"));

        bool found = false;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].IndexOf("cutting the good ones") >= 0) found = true;

        T.Ok(found, "the shape is named: " + string.Join(" / ", lines.ToArray()));
    }

    static void DoingBetterUnderPressureIsAlsoNamed()
    {
        T.S("doing better under pressure is said out loud too");

        // A journal that only ever reports failure is one a trader stops opening.
        List<BallastTrade> sim = new List<BallastTrade>();
        sim.AddRange(Book(6, 100, 5, BallastJournal.Verdict_Chased, -1, 1));
        sim.AddRange(Book(6, -100, 40, BallastJournal.Verdict_Chased, -1, 1));

        List<BallastTrade> real = new List<BallastTrade>();
        real.AddRange(Book(6, 100, 30, BallastJournal.Verdict_ByTheBook, -1, 1));
        real.AddRange(Book(6, -100, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<string> lines = BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded"));

        bool praise = false;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].IndexOf("keep doing it") >= 0 || lines[i].IndexOf("rare") >= 0) praise = true;

        T.Ok(praise, "credit where it is due: " + string.Join(" / ", lines.ToArray()));
    }

    static void NoDifferenceMeansNoSentence()
    {
        T.S("a trader who is the same either way is told nothing");

        List<BallastTrade> sim = new List<BallastTrade>();
        sim.AddRange(Book(6, 100, 20, BallastJournal.Verdict_ByTheBook, -1, 1));
        sim.AddRange(Book(6, -100, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<BallastTrade> real = new List<BallastTrade>();
        real.AddRange(Book(6, 100, 20, BallastJournal.Verdict_ByTheBook, -1, 1));
        real.AddRange(Book(6, -100, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        T.Eq(BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded")).Count, 0,
             "no difference is not a finding, and inventing one would be the worst "
           + "thing this page could do");
    }

    static void MoneyIsNeverTheArgument()
    {
        T.S("money never carries the argument");

        // Identical behaviour, wildly different P&L - the simulator's generous
        // fills. Nothing should be said, because nothing about the PERSON
        // differs.
        List<BallastTrade> sim = new List<BallastTrade>();
        sim.AddRange(Book(6, 5000, 20, BallastJournal.Verdict_ByTheBook, -1, 1));
        sim.AddRange(Book(6, -50, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<BallastTrade> real = new List<BallastTrade>();
        real.AddRange(Book(6, 50, 20, BallastJournal.Verdict_ByTheBook, -1, 1));
        real.AddRange(Book(6, -5000, 10, BallastJournal.Verdict_ByTheBook, -1, 1));

        List<string> lines = BallastJournal.PressureGap(P(sim, "practice"), P(real, "funded"));
        T.Eq(lines.Count, 0,
             "a P&L chasm with identical behaviour says nothing about the trader - "
           + "a simulator's fills flatter you, and an argument that can be explained "
           + "away is not worth having");
    }

    static void BotTradesAndGapsAreNotBehaviour()
    {
        T.S("a strategy has no psychology and a gap has no timestamps");

        List<BallastTrade> mixed = Book(10, 100, 20, BallastJournal.Verdict_ByTheBook, -1, 1);

        for (int i = 0; i < 50; i++)
        {
            BallastTrade bot = Tr(i % 3, -100, 1, BallastJournal.Verdict_Chased, 1, 4);
            bot.Automated = true;
            mixed.Add(bot);
        }

        BallastTrade gap = Tr(0, -900, 60, "", -1, 0);   // no size: reconstructed
        mixed.Add(gap);

        BehaviourProfile p = P(mixed, "practice");
        T.Eq(p.Trades, 10, "only the ten he took by hand count");
        T.Eq(p.OffPlan, 0, "the bot's chasing is not his");
        T.Near(p.AvgContracts, 1, 0.001, "and its size is not his either");
    }
}
