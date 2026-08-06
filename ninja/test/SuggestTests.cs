using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "do we want to give advice or maybe show them articles on how to deal with
/// or what?"
///
/// Neither. Ballast's whole authority rests on everything it says being the
/// trader's own evidence read back to him. The moment it tells him about his
/// mind it is a stranger with no qualifications, and every measurement beside
/// it is worth less.
///
/// What it does instead is point at a number in his own settings that would
/// have changed the outcome, with the evidence underneath. Advice that changes
/// the TOOL, not the person - derived from what he did, and checkable
/// afterwards, because he can watch whether the figure moves.
///
/// These tests are about the bar for opening its mouth. A suggestion built on a
/// handful of trades is worse than none: it spends the credibility that makes
/// the real ones land.
/// </summary>
public static class SuggestTests
{
    public static void Run()
    {
        TheTailOfTheDayIsFound();
        ItWillNotSuggestACapHeAlreadyKeeps();
        ItNeedsEnoughDays();
        TheRevengeWindowIsMeasuredNotAssumed();
        ALosingDayEverywhereIsNotAboutTiming();
        EscalationNeedsFarMoreThanARoughWeek();
    }

    static readonly DateTime D0 = new DateTime(2026, 6, 1, 10, 0, 0);

    static BallastTrade Tr(int day, int numberInDay, double pnl, int minsSinceLoss, int size)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "APEX-11325-106";
        e.Instrument = "NQ SEP26";
        e.MaxContracts = size;
        e.EntryTime = D0.AddDays(day).AddMinutes(numberInDay * 30);
        e.ExitTime = e.EntryTime.AddMinutes(10);
        e.Pnl = pnl;
        e.TradeNumberToday = numberInDay;
        e.MinutesSincePreviousLoss = minsSinceLoss;
        e.PreviousTradeWasLoss = minsSinceLoss >= 0;
        return e;
    }

    static void TheTailOfTheDayIsFound()
    {
        T.S("where in the day the money stops");

        // Ten days. First three trades make money, the next four give it back -
        // the shape almost every discretionary trader has and none can see.
        List<BallastTrade> book = new List<BallastTrade>();
        for (int d = 0; d < 10; d++)
        {
            book.Add(Tr(d, 1, 200, -1, 1));
            book.Add(Tr(d, 2, 150, -1, 1));
            book.Add(Tr(d, 3, 100, -1, 1));
            book.Add(Tr(d, 4, -120, -1, 1));
            book.Add(Tr(d, 5, -140, -1, 1));
            book.Add(Tr(d, 6, -130, -1, 1));
            book.Add(Tr(d, 7, -160, -1, 1));
        }

        SettingSuggestion s = BallastJournal.TradeCountSuggestion(book, 12, 5);
        T.Ok(s != null, "there is something to say");
        T.Eq(s.Kind, "maxtrades", "and it is about the trade count");
        T.Eq(s.Proposed, 3, "stop after the third");
        T.Ok(s.Evidence.IndexOf("$4,500") >= 0, "what the first three made: " + s.Evidence);
        T.Ok(s.Evidence.IndexOf("$5,500") >= 0, "and what the rest gave back");
    }

    static void ItWillNotSuggestACapHeAlreadyKeeps()
    {
        T.S("a suggestion he is already keeping to is noise");

        List<BallastTrade> book = new List<BallastTrade>();
        for (int d = 0; d < 10; d++)
        {
            book.Add(Tr(d, 1, 200, -1, 1));
            book.Add(Tr(d, 2, 150, -1, 1));
            book.Add(Tr(d, 3, 100, -1, 1));
            book.Add(Tr(d, 4, -120, -1, 1));
        }

        T.Ok(BallastJournal.TradeCountSuggestion(book, 3, 5) == null,
             "his limit is already 3 - saying \"stop at 3\" teaches him to ignore the panel");
        T.Ok(BallastJournal.TradeCountSuggestion(book, 2, 5) == null,
             "and it never suggests LOOSENING a limit he has chosen");
    }

    static void ItNeedsEnoughDays()
    {
        T.S("two days is not a pattern");

        List<BallastTrade> book = new List<BallastTrade>();
        for (int d = 0; d < 2; d++)
        {
            book.Add(Tr(d, 1, 500, -1, 1));
            book.Add(Tr(d, 2, -900, -1, 1));
            book.Add(Tr(d, 3, -900, -1, 1));
        }

        T.Ok(BallastJournal.TradeCountSuggestion(book, 12, 5) == null,
             "however dramatic the shape, two days is a bad week and not a habit");
    }

    static void TheRevengeWindowIsMeasuredNotAssumed()
    {
        T.S("how long the damage lasts after a loss");

        // Trades within 20 minutes of a loss bleed; everything else works.
        List<BallastTrade> book = new List<BallastTrade>();
        for (int d = 0; d < 8; d++)
        {
            book.Add(Tr(d, 1, 200, -1, 1));
            book.Add(Tr(d, 2, 180, -1, 1));
            book.Add(Tr(d, 3, -300, 3, 1));
            book.Add(Tr(d, 4, -260, 12, 1));
            book.Add(Tr(d, 5, 150, 90, 1));
        }

        SettingSuggestion s = BallastJournal.CooldownSuggestion(book, 5, 5);
        T.Ok(s != null, "there is a window worth naming");
        T.Eq(s.Kind, "cooldown", "and it is the cooldown");
        T.Ok(s.Proposed >= 15, "wide enough to cover the damage: " + s.Proposed + " min");
        T.Ok(s.Evidence.IndexOf("Your cooldown is 5") >= 0,
             "stated against what he has set: " + s.Evidence);

        T.Ok(BallastJournal.CooldownSuggestion(book, 60, 5) == null,
             "and it says nothing to a trader whose cooldown is already wider");
    }

    static void ALosingDayEverywhereIsNotAboutTiming()
    {
        T.S("a day that lost money everywhere says nothing about timing");

        // Everything loses, in and out of the window, at the same rate. The
        // post-loss trades are negative - but so is everything else, so there is
        // no timing finding here, only a bad stretch.
        List<BallastTrade> book = new List<BallastTrade>();
        for (int d = 0; d < 8; d++)
        {
            book.Add(Tr(d, 1, -200, -1, 1));
            book.Add(Tr(d, 2, -200, 3, 1));
            book.Add(Tr(d, 3, -200, 200, 1));
            book.Add(Tr(d, 4, -200, -1, 1));
        }

        T.Ok(BallastJournal.CooldownSuggestion(book, 5, 5) == null,
             "waiting longer would not have helped, so it does not pretend it would");
    }

    static void EscalationNeedsFarMoreThanARoughWeek()
    {
        T.S("the line that is not about trading needs a great deal of evidence");

        // A bad fortnight at steady size. Nothing to raise.
        List<BallastTrade> steady = new List<BallastTrade>();
        for (int d = 0; d < 15; d++)
            for (int n = 1; n <= 4; n++)
                steady.Add(Tr(d, n, -200, n > 1 ? 5 : -1, 2));

        T.Ok(!BallastJournal.EscalationAfterLosses(steady, 20),
             "losing money is not the pattern - size after a loss is");

        // The same trader, sizing up after every loss, over three weeks.
        List<BallastTrade> chasing = new List<BallastTrade>();
        for (int d = 0; d < 15; d++)
        {
            chasing.Add(Tr(d, 1, 150, -1, 2));      // opens at his normal size
            chasing.Add(Tr(d, 2, -200, -1, 2));
            chasing.Add(Tr(d, 3, -200, 4, 5));      // and then goes after it
            chasing.Add(Tr(d, 4, -200, 4, 6));
            chasing.Add(Tr(d, 5, -200, 4, 8));
        }

        T.Ok(BallastJournal.EscalationAfterLosses(chasing, 20),
             "size going up after losses, sustained across weeks, is the shape");

        // Three days of it is a bad week, not a life.
        List<BallastTrade> shortRun = new List<BallastTrade>();
        for (int d = 0; d < 3; d++)
        {
            shortRun.Add(Tr(d, 1, -200, -1, 2));
            for (int n = 2; n <= 12; n++) shortRun.Add(Tr(d, n, -200, 4, 8));
        }

        T.Ok(!BallastJournal.EscalationAfterLosses(shortRun, 20),
             "and saying this to someone having a rough week is how he stops "
           + "believing anything the software tells him");
    }
}
