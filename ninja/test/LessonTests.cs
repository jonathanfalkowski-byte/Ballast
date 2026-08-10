using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "i noticed i havent really looked at the journal at the end of the day or
/// barely during the day....otherwise it fails to do what it is supposed to do"
///
/// He had been answering every question on every trade for a week and had not
/// once opened the page where the answers add up. That makes the journal
/// tagging overhead - a cost he pays daily for a benefit he never collects.
///
/// So the finding goes to him. These tests are about what it is allowed to SAY,
/// which matters more than that it says anything: a sentence that overclaims on
/// a three-trade sample is how a journal starts lying to a trader who has
/// finally begun reading it.
/// </summary>
public static class LessonTests
{
    public static void Run()
    {
        ExecutionComesFirstBecauseItCanBeActedOn();
        ItWillNotCompareAgainstNothing();
        TheSetupSplitIsTheSecondChoice();
        AFeelingThatOnlyEverLostIsWorthSaying();
        AQuietDayJustReportsItself();
        StrategyTradesAndGapsAreNotEvidence();
        NoTradesMeansNothingToSay();
        SimulatedMoneyIsNeverAddedToRealMoney();
        TheHeadlineIsFactsBeforeAnyExplanation();
        LastSessionIsTheLastDayHeTraded();
    }

    /// <summary>
    /// "could we make Ballast Journal and synopsis of the day week.... better,
    /// right now it feels like a wall of text."
    ///
    /// It was. Four sections, each opening with a paragraph explaining why the
    /// section existed and then reporting that it had nothing to say - roughly
    /// three hundred words to learn that nothing had happened, with the single
    /// real finding buried at the bottom.
    ///
    /// The headline is the fix at the top: what the period WAS, in facts he can
    /// check against his own platform, before a word of method. The method still
    /// exists, behind a link, which is where a thing you read once belongs.
    /// </summary>
    static void TheHeadlineIsFactsBeforeAnyExplanation()
    {
        T.S("the headline is facts, before any explanation");

        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(300, BallastJournal.Verdict_ByTheBook, "A", ""));
        day.Add(Tr(200, BallastJournal.Verdict_ByTheBook, "A", ""));
        day.Add(Tr(-400, BallastJournal.Verdict_Chased, "B", ""));
        day.Add(Tr(-250, BallastJournal.Verdict_Chased, "B", ""));

        string s = BallastJournal.PeriodHeadline(day, JournalPeriod.Today);

        T.Ok(s.IndexOf("Today: 4 trades") >= 0, "the count comes first: " + s);
        T.Ok(s.IndexOf("2 green") >= 0, "then how many worked");
        T.Ok(s.IndexOf("-$150") >= 0, "then the net, which he can check against his platform");
        T.Ok(s.IndexOf("$650") >= 0, "and the one thing that stands out");
        T.Ok(s.IndexOf("$500") >= 0, "with both sides of it");

        // Short enough to be read rather than skimmed past. The old page opened
        // with 340 words of method before a single number.
        T.Ok(s.Length < 220, "and it is one line, not a paragraph: " + s.Length + " chars");

        // The period says which period it is - the old page had a separate
        // "From 7 Aug onward" line doing that job somewhere else entirely.
        T.Ok(BallastJournal.PeriodHeadline(day, JournalPeriod.Week)
                .IndexOf("This week:") >= 0, "a week says it is a week");
        T.Ok(BallastJournal.PeriodHeadline(day, JournalPeriod.Everything)
                .IndexOf("Everything:") >= 0, "and everything says so too");

        // One-sided evidence claims nothing. A period of nothing but planned
        // trades is not proof that going off plan costs money.
        List<BallastTrade> clean = new List<BallastTrade>();
        clean.Add(Tr(-300, BallastJournal.Verdict_ByTheBook, "", ""));
        clean.Add(Tr(-200, BallastJournal.Verdict_ByTheBook, "", ""));
        clean.Add(Tr(-100, BallastJournal.Verdict_ByTheBook, "", ""));

        string q = BallastJournal.PeriodHeadline(clean, JournalPeriod.Today);
        T.Ok(q.IndexOf("3 trades, 0 green") >= 0, "the facts are still stated: " + q);
        T.Ok(q.IndexOf("off your plan") < 0,
             "but nothing is claimed about a comparison that has only one side");

        // Bots and gaps are not his trades and are not in his headline.
        List<BallastTrade> noise = new List<BallastTrade>();
        BallastTrade bot = Tr(-5000, BallastJournal.Verdict_Chased, "", "");
        bot.Automated = true;
        noise.Add(bot);
        BallastTrade gap = Tr(-900, "", "", "");
        gap.MaxContracts = 0;
        noise.Add(gap);
        noise.Add(Tr(120, "", "", ""));

        string n = BallastJournal.PeriodHeadline(noise, JournalPeriod.Today);
        T.Ok(n.IndexOf("1 trade,") >= 0, "only the one he took by hand: " + n);
        T.Ok(n.IndexOf("$120") >= 0, "and only its money");

        // An empty period returns nothing at all, so the page can say so ONCE
        // rather than in every section in a different voice.
        T.Eq(BallastJournal.PeriodHeadline(new List<BallastTrade>(),
                                           JournalPeriod.Today), "",
             "an empty period is one sentence elsewhere, not five here");
        T.Eq(BallastJournal.PeriodHeadline(null, JournalPeriod.Today), "",
             "and no list at all is the same");
    }

    /// <summary>
    /// "at the end of the day it says see the day so i see it and then it pops
    /// me to this page which says month which is a bit misleading"
    ///
    /// The card knew which day it was talking about; the page it opened did not,
    /// and showed whatever period happened to be selected - so "that is the day"
    /// opened a page headed "This month: 87 trades, net -$4,464".
    ///
    /// The closing card now opens on Today and the month card on Month. The
    /// morning card is about the session BEFORE this one, which needed a period
    /// that could say so - and "last session" is not "yesterday", because a
    /// Monday wants Friday.
    /// </summary>
    static void LastSessionIsTheLastDayHeTraded()
    {
        T.S("last session is the last day he traded, not yesterday");

        DateTime monday = new DateTime(2026, 8, 10, 9, 0, 0);

        List<BallastTrade> book = new List<BallastTrade>();
        book.Add(At(Tr(300, BallastJournal.Verdict_ByTheBook, "", ""), monday.AddDays(-3)));  // Fri
        book.Add(At(Tr(-120, BallastJournal.Verdict_Chased, "", ""), monday.AddDays(-3)));    // Fri
        book.Add(At(Tr(900, BallastJournal.Verdict_ByTheBook, "", ""), monday.AddDays(-7)));  // week before
        book.Add(At(Tr(50, BallastJournal.Verdict_ByTheBook, "", ""), monday));               // today

        List<BallastTrade> last = BallastJournal.InPeriod(book, monday, JournalPeriod.LastSession);
        T.Eq(last.Count, 2, "Friday's two trades, over a weekend with nothing in it");

        double net = 0;
        for (int i = 0; i < last.Count; i++) net += last[i].Pnl;
        T.Near(net, 180, 0.01, "and only Friday's money");

        // Today is excluded - the morning card is about the session before this
        // one, and today has barely started.
        for (int i = 0; i < last.Count; i++)
            T.Ok(last[i].ExitTime.Date != monday.Date, "nothing from this morning is in it");

        T.Eq(BallastJournal.PeriodName(JournalPeriod.LastSession), "last session", "it has a name");
        T.Ok(BallastJournal.PeriodHeadline(last, JournalPeriod.LastSession)
                .IndexOf("Last session: 2 trades") >= 0,
             "and the headline uses it: "
           + BallastJournal.PeriodHeadline(last, JournalPeriod.LastSession));

        // A first-ever day has no previous session, and says nothing rather
        // than falling back to something else.
        List<BallastTrade> firstDay = new List<BallastTrade>();
        firstDay.Add(At(Tr(50, BallastJournal.Verdict_ByTheBook, "", ""), monday));
        T.Eq(BallastJournal.InPeriod(firstDay, monday, JournalPeriod.LastSession).Count, 0,
             "no earlier session means no trades, not yesterday's absence of them");

        // Today still means today.
        T.Eq(BallastJournal.InPeriod(book, monday, JournalPeriod.Today).Count, 1,
             "and Today is untouched by any of this");
    }

    static BallastTrade At(BallastTrade e, DateTime when)
    {
        e.EntryTime = when;
        e.ExitTime = when.AddMinutes(3);
        return e;
    }

    static readonly DateTime D = new DateTime(2026, 8, 6, 10, 0, 0);

    static BallastTrade Tr(double pnl, string planned, string setup, string feeling)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "APEX-11325-106";
        e.Instrument = "NQ SEP26";
        e.EntryTime = D;
        e.ExitTime = D.AddMinutes(2);
        e.MaxContracts = 1;          // a real, watched trade - see IsReconstructed
        e.Pnl = pnl;
        e.Planned = planned;
        e.Setup = setup;
        e.Feeling = feeling;
        return e;
    }

    static void ExecutionComesFirstBecauseItCanBeActedOn()
    {
        T.S("the day's lesson leads with what can be changed tomorrow");

        // A day with a clear execution story AND a clear setup story. Execution
        // wins, because "stop chasing" is an instruction and "setup B is weaker"
        // is a research note.
        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(300, BallastJournal.Verdict_ByTheBook, "A", ""));
        day.Add(Tr(200, BallastJournal.Verdict_ByTheBook, "A", ""));
        day.Add(Tr(-400, BallastJournal.Verdict_Chased, "B", ""));
        day.Add(Tr(-250, BallastJournal.Verdict_Chased, "B", ""));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("off your plan") >= 0, "it names the execution, not the setup: " + s);
        T.Ok(s.IndexOf("$650") >= 0, "and what it cost");
        T.Ok(s.IndexOf("$500") >= 0, "against what keeping to it made");
    }

    static void ItWillNotCompareAgainstNothing()
    {
        T.S("a day with only planned trades is not evidence about chasing");

        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(-300, BallastJournal.Verdict_ByTheBook, "", ""));
        day.Add(Tr(-200, BallastJournal.Verdict_ByTheBook, "", ""));
        day.Add(Tr(-100, BallastJournal.Verdict_ByTheBook, "", ""));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("off your plan") < 0,
             "nothing was taken off plan, so nothing is claimed about it: " + s);
        T.Ok(s.IndexOf("3 trades") >= 0, "it falls back to reporting the day: " + s);

        // One chased trade is an anecdote, not a comparison.
        List<BallastTrade> thin = new List<BallastTrade>();
        thin.Add(Tr(200, BallastJournal.Verdict_ByTheBook, "", ""));
        thin.Add(Tr(200, BallastJournal.Verdict_ByTheBook, "", ""));
        thin.Add(Tr(-900, BallastJournal.Verdict_Chased, "", ""));
        T.Ok(BallastJournal.DayLesson(thin).IndexOf("off your plan") < 0,
             "one chased trade, however expensive, is not a pattern");
    }

    static void TheSetupSplitIsTheSecondChoice()
    {
        T.S("with no execution story, the setups get to speak");

        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(400, BallastJournal.Verdict_ByTheBook, "A - EMA cross", ""));
        day.Add(Tr(150, BallastJournal.Verdict_ByTheBook, "A - EMA cross", ""));
        day.Add(Tr(-300, BallastJournal.Verdict_ByTheBook, "B - pivot", ""));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("A - EMA cross") >= 0 && s.IndexOf("B - pivot") >= 0,
             "both setups are named: " + s);
        T.Ok(s.IndexOf("$550") >= 0, "with what the good one made");
        T.Ok(s.IndexOf("$300") >= 0, "and what the bad one cost");
    }

    static void AFeelingThatOnlyEverLostIsWorthSaying()
    {
        T.S("a feeling that only ever lost is worth naming");

        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(-200, BallastJournal.Verdict_ByTheBook, "", "Wanted it back"));
        day.Add(Tr(-150, BallastJournal.Verdict_ByTheBook, "", "Wanted it back"));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("Wanted it back") >= 0, "the feeling is quoted back: " + s);
        T.Ok(s.IndexOf("$350") >= 0, "with what it cost");
        T.Ok(s.IndexOf("size up") >= 0, "and what to do about it");
    }

    static void AQuietDayJustReportsItself()
    {
        T.S("a day with nothing to compare reports itself and no more");

        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Tr(120, "", "", ""));
        day.Add(Tr(-40, "", "", ""));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("2 trades") >= 0, "the count: " + s);
        T.Ok(s.IndexOf("1 green") >= 0, "how many worked");
        T.Ok(s.IndexOf("$80") >= 0, "and the net");
        T.Ok(s.IndexOf("cost") < 0, "with no claim about why");
    }

    static void StrategyTradesAndGapsAreNotEvidence()
    {
        T.S("bot trades and reconstructed gaps are left out");

        List<BallastTrade> day = new List<BallastTrade>();

        BallastTrade bot = Tr(-5000, BallastJournal.Verdict_Chased, "", "");
        bot.Automated = true;
        day.Add(bot);

        BallastTrade gap = Tr(-900, "", "", "");
        gap.Instrument = "(Ballast was closed)";
        gap.MaxContracts = 0;        // no size is what makes a row reconstructed
        day.Add(gap);

        day.Add(Tr(120, "", "", ""));
        day.Add(Tr(-40, "", "", ""));

        string s = BallastJournal.DayLesson(day);
        T.Ok(s.IndexOf("2 trades") >= 0,
             "only the two he actually took by hand are counted: " + s);
        T.Ok(s.IndexOf("$80") >= 0,
             "and the bot's loss is not laid at his door - a strategy has no "
           + "discipline to report on, and a gap has no detail to report");
    }

    /// <summary>
    /// "did you account for multiple accounts or did you put them all in
    /// ...(you took all the trades from all the accounts and put them in one
    /// report or.....)"
    ///
    /// He caught it. The first version pooled every watched account into one
    /// sentence, which on his own screen would have added nine sim trades to two
    /// funded ones and reported the sum in dollars. Play money and real money in
    /// the same figure, with the sim - where the volume always is - deciding what
    /// the day "showed" while the accounts that can actually be lost went
    /// unmentioned.
    ///
    /// Behaviour still pools across REAL accounts, because chasing is the
    /// trader's habit rather than the account's, and a dollar is a dollar across
    /// two funded accounts. It does not pool across the sim line.
    /// </summary>
    static void SimulatedMoneyIsNeverAddedToRealMoney()
    {
        T.S("simulated money is never added to real money");

        List<BallastTrade> day = new List<BallastTrade>();

        // Two funded accounts, one habit: pooling THESE is right.
        BallastTrade a = Tr(-400, BallastJournal.Verdict_Chased, "", "");
        a.AccountName = "APEX-11325-105";
        BallastTrade b = Tr(-250, BallastJournal.Verdict_Chased, "", "");
        b.AccountName = "APEX-11325-106";
        BallastTrade c = Tr(300, BallastJournal.Verdict_ByTheBook, "", "");
        c.AccountName = "APEX-11325-105";
        BallastTrade d = Tr(200, BallastJournal.Verdict_ByTheBook, "", "");
        d.AccountName = "APEX-11325-106";
        day.Add(a); day.Add(b); day.Add(c); day.Add(d);

        // And a busy sim day that would otherwise drown them.
        for (int i = 0; i < 9; i++)
        {
            BallastTrade s = Tr(-1000, BallastJournal.Verdict_ByTheBook, "", "");
            s.AccountName = "Sim103";
            day.Add(s);
        }

        List<string> sims = new List<string>();
        sims.Add("Sim103");

        string real = BallastJournal.DayLesson(
            BallastJournal.FromAccounts(day, sims, false));

        T.Ok(real.IndexOf("$650") >= 0,
             "the funded accounts are pooled with each other: " + real);
        T.Ok(real.IndexOf("$500") >= 0, "on both sides of the comparison");
        T.Ok(real.IndexOf("9,000") < 0 && real.IndexOf("8,500") < 0,
             "and the sim's nine thousand is nowhere in it");

        string sim = BallastJournal.DayLesson(
            BallastJournal.FromAccounts(day, sims, true));
        T.Ok(sim.IndexOf("9 trades") >= 0, "the sim gets its own reckoning: " + sim);
        T.Ok(sim.IndexOf("APEX") < 0, "with none of the real money in it");

        // And the split itself, both ways round.
        T.Eq(BallastJournal.FromAccounts(day, sims, true).Count, 9, "nine sim trades");
        T.Eq(BallastJournal.FromAccounts(day, sims, false).Count, 4, "four real ones");
        T.Eq(BallastJournal.FromAccounts(day, null, false).Count, 13,
             "no named accounts means nothing is excluded");
    }

    static void NoTradesMeansNothingToSay()
    {
        T.S("a day with no trades says nothing at all");

        T.Eq(BallastJournal.DayLesson(new List<BallastTrade>()), "",
             "silence, rather than a cheerful nothing");
        T.Eq(BallastJournal.DayLesson(null), "", "and the same for no list at all");
    }
}
