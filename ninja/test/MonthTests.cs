using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "so after a month we will need to show the user some stats to know whether
/// they are improving or not. What do you think?"
///
/// The obvious version would do harm, and these tests are mostly about what the
/// report REFUSES to say.
///
/// A month is about a hundred trades. At that sample the difference between a
/// green month and a red one is mostly the market - so a P&L trend presented as
/// improvement teaches a trader to feel skilled in a lucky month and broken in
/// an unlucky one, which is the exact psychology the rest of Ballast argues
/// with. What improves measurably in a month is behaviour: counts of decisions
/// he made, with no market in them.
/// </summary>
public static class MonthTests
{
    public static void Run()
    {
        CleanSessionsAreWholeDaysHeldTogether();
        ANoisyWobbleIsNotProgress();
        ARealChangeIsNamed();
        MoneyIsNeverTheScore();
        AShortMonthIsNotCompared();
        OneDefinitionOfCleanEverywhere();
    }

    static DateTime Day(int month, int day) { return new DateTime(2026, month, day, 10, 0, 0); }

    static BallastTrade Tr(DateTime at, int numberInDay, double pnl, string planned,
                           string advice, int minsSinceLoss)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "APEX-11325-105";
        e.Instrument = "NQ SEP26";
        e.MaxContracts = 1;
        e.EntryTime = at;
        e.ExitTime = at.AddMinutes(2);
        e.Pnl = pnl;
        e.TradeNumberToday = numberInDay;
        e.Planned = planned;
        e.AdviceAtEntry = advice;
        e.MinutesSincePreviousLoss = minsSinceLoss;
        e.PreviousTradeWasLoss = minsSinceLoss >= 0;
        return e;
    }

    static BallastTrade Good(DateTime at, int n, double pnl)
    {
        return Tr(at, n, pnl, BallastJournal.Verdict_ByTheBook, "Trade", -1);
    }

    static BallastTrade Bad(DateTime at, int n, double pnl)
    {
        return Tr(at, n, pnl, BallastJournal.Verdict_Chased, "Cooldown", 3);
    }

    /// <summary>
    /// The headline metric, and the reason it is the headline: one chased trade
    /// spoils the day. A percentage can be rescued by a good afternoon; a day
    /// held together cannot be, and days held together are what compound.
    /// </summary>
    static void CleanSessionsAreWholeDaysHeldTogether()
    {
        T.S("a clean session is a whole day held together");

        List<BallastTrade> book = new List<BallastTrade>();

        // Three days: one perfect, one spoiled by a single chased trade at the
        // end, one perfect.
        for (int n = 1; n <= 4; n++) book.Add(Good(Day(7, 1).AddMinutes(n * 20), n, 100));
        for (int n = 1; n <= 3; n++) book.Add(Good(Day(7, 2).AddMinutes(n * 20), n, 100));
        book.Add(Bad(Day(7, 2).AddMinutes(90), 4, -400));
        for (int n = 1; n <= 2; n++) book.Add(Good(Day(7, 3).AddMinutes(n * 20), n, 100));

        MonthStats m = MonthReport.For(book, Day(7, 1), 12, 15);

        T.Eq(m.Sessions, 3, "three days were traded");
        T.Eq(m.CleanSessions, 2, "one of them was spoiled by a single trade");
        T.Eq(m.Trades, 10, "ten trades in the month");
        T.Eq(m.Clean, 9, "nine of which broke nothing");
        T.Eq(m.OffPlan, 1, "and one he called chased himself");

        // Trades from another month are not in it.
        book.Add(Bad(Day(8, 1), 1, -900));
        MonthStats july = MonthReport.For(book, Day(7, 15), 12, 15);
        T.Eq(july.Trades, 10, "August is not in July's figures");
        T.Eq(july.Sessions, 3, "nor in its sessions");
    }

    /// <summary>
    /// The verdict this report will give most often, and the one that makes the
    /// others worth reading.
    /// </summary>
    static void ANoisyWobbleIsNotProgress()
    {
        T.S("a small wobble is not progress");

        // 100 trades a month, 30 chased then 26 chased. It looks like an
        // improvement and it is nothing.
        MonthStats a = Fake(20, 8, 100, 30);
        MonthStats b = Fake(20, 10, 100, 26);
        b.Month = new DateTime(2026, 8, 1);

        T.Ok(!MonthReport.Moved(30, 100, 26, 100),
             "four fewer chased trades out of a hundred does not clear the bar");
        T.Ok(!MonthReport.Moved(8, 20, 10, 20),
             "and two more clean days out of twenty does not either");

        string s = MonthReport.Compare(a, b);
        T.Ok(s.IndexOf("No real change") >= 0, "so it says so plainly: " + s);
        T.Ok(s.IndexOf("worth nothing by its second issue") >= 0,
             "and says why it is refusing, so the silence is not read as a fault");

        // The facts are still reported even when the verdict is nothing.
        T.Ok(s.IndexOf("8 of 20") >= 0 && s.IndexOf("10 of 20") >= 0,
             "the clean-session counts are always stated: " + s);
    }

    static void ARealChangeIsNamed()
    {
        T.S("a real change is named");

        MonthStats a = Fake(20, 4, 100, 40);
        MonthStats b = Fake(20, 15, 100, 8);
        b.Month = new DateTime(2026, 8, 1);

        T.Ok(MonthReport.Moved(4, 20, 15, 20), "four clean days to fifteen is a real move");

        string s = MonthReport.Compare(a, b);
        T.Ok(s.IndexOf("more whole days together") >= 0, "and it is named: " + s);
        T.Ok(s.IndexOf("compounds") >= 0, "with why that is the one that matters");

        // Backwards is reported as honestly as forwards.
        string worse = MonthReport.Compare(b, a);
        T.Ok(worse.IndexOf("Fewer whole days") >= 0, "going backwards is said too: " + worse);
        T.Ok(worse.IndexOf("before it becomes the habit") >= 0, "without scolding him for it");
    }

    /// <summary>
    /// The whole argument of the file. A month of P&L is mostly the market, and
    /// presenting it as a score is how a tool teaches a trader to read luck as
    /// skill.
    /// </summary>
    static void MoneyIsNeverTheScore()
    {
        T.S("money is never the score");

        // Identical behaviour, wildly different months. The report must read the
        // same, because he did the same things.
        List<BallastTrade> lucky = new List<BallastTrade>();
        List<BallastTrade> unlucky = new List<BallastTrade>();

        for (int d = 1; d <= 12; d++)
            for (int n = 1; n <= 5; n++)
            {
                lucky.Add(Good(Day(7, d).AddMinutes(n * 20), n, 400));
                unlucky.Add(Good(Day(7, d).AddMinutes(n * 20), n, -400));
            }

        MonthStats g = MonthReport.For(lucky, Day(7, 1), 12, 15);
        MonthStats r = MonthReport.For(unlucky, Day(7, 1), 12, 15);

        T.Eq(g.CleanSessions, r.CleanSessions, "a green month and a red month with the "
                                             + "same behaviour have the same clean sessions");
        T.Eq(g.Clean, r.Clean, "and the same clean trades");
        T.Near(g.CleanTradeRate, r.CleanTradeRate, 0.001, "and the same rate");

        // The verdict between two such months says nothing about the money.
        MonthStats later = MonthReport.For(unlucky, Day(7, 1), 12, 15);
        later.Month = new DateTime(2026, 8, 1);
        string s = MonthReport.Compare(g, later);

        T.Ok(s.IndexOf("No real change") >= 0,
             "sixty winners becoming sixty losers is NOT a behaviour finding: " + s);
        T.Ok(s.IndexOf("P&L is not in this") >= 0,
             "and the absence of P&L is explained rather than left looking like an oversight");
        T.Ok(s.IndexOf("24,000") < 0 && s.IndexOf("$24") < 0,
             "the month's money is nowhere in the report");
    }

    static void AShortMonthIsNotCompared()
    {
        T.S("a fortnight is not a month");

        MonthStats thin = Fake(5, 1, 20, 12);
        MonthStats full = Fake(20, 15, 100, 8);
        full.Month = new DateTime(2026, 8, 1);

        T.Eq(MonthReport.Compare(thin, full), "",
             "five sessions is not enough to compare anything against");
        T.Eq(MonthReport.Compare(full, thin), "", "in either direction");

        MonthStats empty = Fake(0, 0, 0, 0);
        T.Eq(MonthReport.Compare(empty, full), "", "and a month with nothing in it says nothing");
        T.Eq(MonthReport.Compare(null, full), "", "nor does no month at all");
    }

    /// <summary>
    /// The practice score and the month report both ask "did this break a rule".
    /// Two answers to that question would eventually disagree, and the first he
    /// would hear of it is a month praising a session the practice book marked
    /// down. There is one definition and both use it.
    /// </summary>
    static void OneDefinitionOfCleanEverywhere()
    {
        T.S("one definition of clean, everywhere");

        BallastTrade chased = Bad(Day(7, 1), 2, -300);
        BallastTrade clean = Good(Day(7, 1), 1, 200);

        T.Ok(BallastJournal.BrokeARule(chased, 12, 15), "a chased trade inside the cooldown broke a rule");
        T.Ok(!BallastJournal.BrokeARule(clean, 12, 15), "a planned trade with a clear signal did not");

        // Silence is not an admission. Counting untagged trades as broken would
        // punish him for the days he was too busy trading to tag.
        BallastTrade untagged = Tr(Day(7, 1), 1, -100, "", "Trade", -1);
        T.Ok(!BallastJournal.BrokeARule(untagged, 12, 15), "and an untagged trade is not held against him");

        T.Ok(BallastJournal.BrokeARule(Tr(Day(7, 1), 13, 50, BallastJournal.Verdict_ByTheBook, "Trade", -1), 12, 15),
             "past the count is a broken rule however well the trade went");
        T.Ok(!BallastJournal.BrokeARule(null, 12, 15), "and nothing is not a broken rule");

        // The practice score agrees, because it asks the same function.
        PracticeRun run = new PracticeRun();
        run.Trades.Add(chased);
        run.Trades.Add(clean);
        PracticeScore ps = PracticeBook.Score(run, 12, 15);
        T.Eq(ps.Clean, 1, "the practice book counts one clean trade");

        List<BallastTrade> book = new List<BallastTrade>();
        book.Add(chased); book.Add(clean);
        MonthStats m = MonthReport.For(book, Day(7, 1), 12, 15);
        T.Eq(m.Clean, 1, "and the month report counts the same one");
    }

    /// <summary>Stats with the shape a real month would have, for the verdict tests.</summary>
    static MonthStats Fake(int sessions, int cleanSessions, int trades, int broken)
    {
        MonthStats m = new MonthStats();
        m.Month = new DateTime(2026, 7, 1);
        m.Sessions = sessions;
        m.CleanSessions = cleanSessions;
        m.Trades = trades;
        m.Clean = trades - broken;
        m.OffPlan = broken;
        return m;
    }
}
