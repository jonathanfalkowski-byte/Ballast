using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "does looking at the previous days trade help you trade better? Does the
/// psychologiists say that? if it does, then how do we make the user look at
/// their past trades?"
///
/// It helps, and there is a literature. Di Stefano, Gino, Pisano and Staats
/// found reflection on experience can beat more practice, most of all early on
/// the learning curve. But Kluger and DeNisi found over a third of feedback
/// interventions make performance WORSE, and the discriminator is where the
/// feedback points: at the task it helps, at the SELF it hurts.
///
/// So these tests check two things. That the right trade is chosen - the one
/// whose outcome disagrees with the decision. And that nothing said about it is
/// ever about him.
/// </summary>
public static class LookBackTests
{
    public static void Run()
    {
        ARewardedMistakeOutranksEverything();
        AGoodTradeThatLostIsTheOtherHalf();
        NothingSaidIsAboutTheTrader();
        TodayIsNotHindsight();
        TheSameTradeIsNotShownTwice();
        AnHonestNothingToShow();
    }

    static readonly DateTime Now = new DateTime(2026, 8, 10, 8, 0, 0);

    static BallastTrade Tr(int daysAgo, double pnl, string planned, string advice,
                           int numberInDay, int minsSinceLoss)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "APEX-11325-105";
        e.Instrument = "NQ SEP26";
        e.MaxContracts = 2;
        e.EntryTime = Now.Date.AddDays(-daysAgo).AddHours(10);
        e.ExitTime = e.EntryTime.AddMinutes(4);
        e.Pnl = pnl;
        e.Planned = planned;
        e.AdviceAtEntry = advice;
        e.TradeNumberToday = numberInDay;
        e.MinutesSincePreviousLoss = minsSinceLoss;
        e.PreviousTradeWasLoss = minsSinceLoss >= 0;
        return e;
    }

    /// <summary>
    /// The most expensive row in any journal is not the biggest loser. It is the
    /// trade that broke a rule and paid, because that is the one that builds the
    /// habit which eventually costs the account.
    /// </summary>
    static void ARewardedMistakeOutranksEverything()
    {
        T.S("a trade that broke a rule and won is the one worth looking at");

        List<BallastTrade> book = new List<BallastTrade>();
        book.Add(Tr(2, -900, BallastJournal.Verdict_ByTheBook, "Trade", 1, -1));   // clean, lost big
        book.Add(Tr(3, 570, BallastJournal.Verdict_Chased, "Cooldown", 4, 3));     // broke, won
        book.Add(Tr(1, 400, BallastJournal.Verdict_ByTheBook, "Trade", 1, -1));    // clean, won

        LookBackPick p = LookBack.Pick(book, Now, 12, 15, null);
        T.Ok(p != null, "there is something to show");
        T.Ok(p.RewardedAMistake, "and it is the rule-breaker that paid");
        T.Near(p.Trade.Pnl, 570, 0.01, "the one that made 570");

        // The biggest win among rule-breakers, not merely the first found.
        book.Add(Tr(4, 1200, BallastJournal.Verdict_Chased, "Cooldown", 6, 2));
        p = LookBack.Pick(book, Now, 12, 15, null);
        T.Near(p.Trade.Pnl, 1200, 0.01, "the loudest one wins");

        string reveal = LookBack.Reveal(p);
        T.Ok(reveal.IndexOf("most expensive row") >= 0, "and it says why: " + reveal);
        T.Ok(reveal.IndexOf("because the market moved your way") >= 0,
             "naming the bias it is there to defeat");
    }

    static void AGoodTradeThatLostIsTheOtherHalf()
    {
        T.S("a trade by the book that lost is the other half");

        List<BallastTrade> book = new List<BallastTrade>();
        book.Add(Tr(2, -900, BallastJournal.Verdict_ByTheBook, "Trade", 1, -1));
        book.Add(Tr(1, 400, BallastJournal.Verdict_ByTheBook, "Trade", 1, -1));
        book.Add(Tr(3, -200, BallastJournal.Verdict_Chased, "Cooldown", 5, 2));   // broke AND lost

        LookBackPick p = LookBack.Pick(book, Now, 12, 15, null);
        T.Ok(p != null && p.PunishedThePlan, "with no rewarded mistake, the punished plan is shown");
        T.Near(p.Trade.Pnl, -900, 0.01, "the worst of them");

        string reveal = LookBack.Reveal(p);
        T.Ok(reveal.IndexOf("not evidence the plan is wrong") >= 0,
             "and it protects the rule rather than the feeling: " + reveal);

        // A trade that broke a rule AND lost teaches nothing new - he already
        // knows, it hurt at the time. It is never the pick.
        List<BallastTrade> onlyBad = new List<BallastTrade>();
        onlyBad.Add(Tr(3, -200, BallastJournal.Verdict_Chased, "Cooldown", 5, 2));
        T.Ok(LookBack.Pick(onlyBad, Now, 12, 15, null) == null,
             "a mistake that was punished is not misleading, so it is not shown");
    }

    /// <summary>
    /// Kluger and DeNisi: feedback aimed at the SELF degrades performance. This
    /// is the test that keeps that honest, and it is deliberately blunt.
    /// </summary>
    static void NothingSaidIsAboutTheTrader()
    {
        T.S("nothing said is about the trader");

        List<BallastTrade> book = new List<BallastTrade>();
        BallastTrade e = Tr(3, 570, BallastJournal.Verdict_Chased, "Cooldown", 4, 3);
        e.Setup = "B - pivot, first dot";
        e.Note = "i just wanted my money back";
        book.Add(e);

        LookBackPick p = LookBack.Pick(book, Now, 12, 15, null);
        string q = LookBack.Question(p, 12, 15);
        string r = LookBack.Reveal(p);
        string both = (q + " " + r).ToLowerInvariant();

        string[] banned = new string[] {
            "you are", "you're", "undisciplined", "impulsive", "reckless",
            "you always", "you never", "discipline problem", "you failed",
            "bad habit of yours", "your problem"
        };
        for (int i = 0; i < banned.Length; i++)
            T.Ok(both.IndexOf(banned[i]) < 0,
                 "never says \"" + banned[i] + "\" - that is feedback about the person, "
               + "and a third of those make performance worse");

        // It states the FACT about the trade instead.
        T.Ok(q.IndexOf("3 minutes after a loss") >= 0,
             "the rule it broke is a fact about the trade: " + q);
        T.Ok(q.IndexOf("cooldown of 15") >= 0, "against the number he set");
        T.Ok(q.IndexOf("B - pivot, first dot") >= 0, "his own setup name");

        // And the outcome is NOT in the question.
        T.Ok(q.IndexOf("570") < 0 && q.IndexOf("made") < 0,
             "the outcome is withheld until he has answered: " + q);
        T.Ok(q.IndexOf("entry only") >= 0, "and it says that is what he is looking at");
        T.Ok(r.IndexOf("570") >= 0, "the reveal has it");
        T.Ok(r.IndexOf("i just wanted my money back") >= 0, "with his own words back");
    }

    static void TodayIsNotHindsight()
    {
        T.S("this morning is not hindsight");

        List<BallastTrade> book = new List<BallastTrade>();
        BallastTrade today = Tr(0, 900, BallastJournal.Verdict_Chased, "Cooldown", 5, 2);
        book.Add(today);
        T.Ok(LookBack.Pick(book, Now, 12, 15, null) == null,
             "a trade taken this morning has not been slept on and teaches nothing yet");

        // And beyond the window the charts have been deleted, so there is
        // nothing to show even if the row survives.
        List<BallastTrade> old = new List<BallastTrade>();
        old.Add(Tr(40, 900, BallastJournal.Verdict_Chased, "Cooldown", 5, 2));
        T.Ok(LookBack.Pick(old, Now, 12, 15, null) == null, "and forty days back is out of reach");
    }

    static void TheSameTradeIsNotShownTwice()
    {
        T.S("the same trade is not shown twice");

        List<BallastTrade> book = new List<BallastTrade>();
        BallastTrade a = Tr(3, 570, BallastJournal.Verdict_Chased, "Cooldown", 4, 3);
        BallastTrade b = Tr(4, 300, BallastJournal.Verdict_Chased, "Cooldown", 5, 2);
        book.Add(a); book.Add(b);

        LookBackPick first = LookBack.Pick(book, Now, 12, 15, null);
        T.Near(first.Trade.Pnl, 570, 0.01, "the loudest first");

        List<string> shown = new List<string>();
        shown.Add(LookBack.KeyOf(first.Trade));

        LookBackPick second = LookBack.Pick(book, Now, 12, 15, shown);
        T.Ok(second != null, "there is a second one");
        T.Near(second.Trade.Pnl, 300, 0.01, "and it is the next loudest");

        shown.Add(LookBack.KeyOf(second.Trade));
        T.Ok(LookBack.Pick(book, Now, 12, 15, shown) == null,
             "and when they are all seen it stops rather than repeating");

        T.Ok(LookBack.KeyOf(a) != LookBack.KeyOf(b), "two trades have two keys");
        T.Eq(LookBack.KeyOf(null), "", "and nothing has none");
    }

    static void AnHonestNothingToShow()
    {
        T.S("nothing to show is a real answer");

        T.Ok(LookBack.Pick(new List<BallastTrade>(), Now, 12, 15, null) == null,
             "an empty journal has nothing to teach");
        T.Ok(LookBack.Pick(null, Now, 12, 15, null) == null, "and neither has no journal");
        T.Eq(LookBack.Question(null, 12, 15), "", "no pick, no question");
        T.Eq(LookBack.Reveal(null), "", "no pick, no reveal");

        // A week of clean, winning trades is a good week and has nothing
        // misleading in it. It must not manufacture a lesson.
        List<BallastTrade> good = new List<BallastTrade>();
        for (int d = 1; d <= 5; d++)
            good.Add(Tr(d, 300, BallastJournal.Verdict_ByTheBook, "Trade", 1, -1));
        T.Ok(LookBack.Pick(good, Now, 12, 15, null) == null,
             "a good week is left alone rather than picked at");

        // Bots are not his decisions.
        List<BallastTrade> bot = new List<BallastTrade>();
        BallastTrade b = Tr(2, 900, BallastJournal.Verdict_Chased, "Cooldown", 9, 1);
        b.Automated = true;
        bot.Add(b);
        T.Ok(LookBack.Pick(bot, Now, 12, 15, null) == null, "a strategy has no lesson to learn");
    }
}
