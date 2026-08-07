using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "i ws practicing on the playtest connection... it is all about testing things
/// and practicing is there something we can add to that...do we even want to
/// record that or is there something else we can do ...to help the trader
/// realize his mistakes or patterns?"
///
/// The first job is not to help - it is not to do harm. Replay trades carry the
/// REPLAY clock, so a trade taken while replaying the sixth of August is stamped
/// the sixth of August and lands in the journal beside the real trades taken
/// that morning on a funded account. That is the failure these tests exist to
/// make impossible.
///
/// The second job is the interesting one. Replay is the only place a rule can be
/// TESTED rather than followed, because the same morning can be run twice with
/// the bars held identical - so the only thing that changed is him.
/// </summary>
public static class PracticeTests
{
    public static void Run()
    {
        AReplayIsNeverTheSameAsATrade();
        ARewindStartsAFreshAttempt();
        TheScoreIsBehaviourNeverMoney();
        SilenceIsNotCountedAsAGoodTrade();
        TheSameSessionTwiceIsAControlledTest();
        ItWillNotCallOneTradeProgress();
        NothingToCompareSaysNothing();
        ReplayNeverReachesTheJournal();
    }

    /// <summary>
    /// The accident this whole file exists to prevent, tested at the one door
    /// where it could happen.
    ///
    /// Replaying the sixth of August produces trades stamped the sixth of
    /// August. If they reach the journal they sit beside the funded trades taken
    /// that morning on APEX-11325-106 - same date, same instrument, no way to
    /// tell them apart - and every answer built on that journal quietly absorbs
    /// them.
    /// </summary>
    static void ReplayNeverReachesTheJournal()
    {
        T.S("a replay never reaches the journal");

        BallastMonitor m = new BallastMonitor();

        BallastTracker real = m.GetOrCreate("APEX-11325-106");
        real.Config = new TrackerConfig();
        real.Config.TrustAccountRealised = false;
        real.EnsureSession(Session, 0, 250000);
        real.OnEquity(250000, 0);

        BallastTracker play = m.GetOrCreate("Playback101");
        play.Config = new TrackerConfig();
        play.Config.TrustAccountRealised = false;
        play.EnsureSession(Session, 0, 100000);
        play.OnEquity(100000, 0);

        // One real trade on the funded account.
        m.OnPosition("APEX-11325-106", 1, 0, Session.AddMinutes(5), "NQ SEP26");
        BallastTrade r = m.OnPosition("APEX-11325-106", 0, -400, Session.AddMinutes(9), "NQ SEP26");
        T.Ok(r != null, "the funded round trip closed");

        // And a replay of the SAME MORNING, which is what makes this dangerous.
        m.OnPosition("Playback101", 1, 0, Session.AddMinutes(5), "NQ SEP26");
        BallastTrade p = m.OnPosition("Playback101", 0, -900, Session.AddMinutes(9), "NQ SEP26");
        T.Ok(p != null, "the replayed round trip closed too");

        T.Eq(m.Journal.Count, 1, "the journal holds the real trade and only the real trade");
        T.Eq(m.Journal.All[0].AccountName, "APEX-11325-106", "and it is the funded one");

        PracticeRun run = m.Practice.Latest("Playback101");
        T.Ok(run != null, "the replay went to the practice book");
        T.Eq(run.Trades.Count, 1, "with the trade in it");
        T.Eq(run.SessionDate, Session.Date, "filed under the morning being replayed");

        // The journal's own reading of that day must be untouched by the replay.
        List<BallastTrade> day = m.Journal.All;
        double net = 0;
        for (int i = 0; i < day.Count; i++) net += day[i].Pnl;
        T.Near(net, -400, 0.01,
               "the day nets what he actually lost, not what he lost plus what he practised");
    }

    static readonly DateTime Session = new DateTime(2026, 8, 6, 9, 30, 0);
    static readonly DateTime RealNow = new DateTime(2026, 8, 7, 19, 0, 0);

    static BallastTrade Tr(int numberInDay, int minsSinceLoss, bool prevLoss,
                           string advice, string planned)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "Playback101";
        e.Instrument = "NQ SEP26";
        e.MaxContracts = 1;
        e.EntryTime = Session.AddMinutes(numberInDay * 6);
        e.ExitTime = e.EntryTime.AddMinutes(2);
        e.TradeNumberToday = numberInDay;
        e.MinutesSincePreviousLoss = minsSinceLoss;
        e.PreviousTradeWasLoss = prevLoss;
        e.AdviceAtEntry = advice;
        e.Planned = planned;
        e.Pnl = -250;               // deliberately: money must not move the score
        return e;
    }

    /// <summary>
    /// The whole reason practice has its own book. A replayed sixth of August is
    /// dated the sixth of August, so in one shared journal it would sit beside
    /// the funded trades taken that morning and be counted as evidence about
    /// them.
    /// </summary>
    static void AReplayIsNeverTheSameAsATrade()
    {
        T.S("a replayed morning is not evidence about a real one");

        T.Ok(RuleBook.IsPracticeAccountName("Playback101"),
             "the playback account is recognised as practice");
        T.Ok(RuleBook.IsPracticeAccountName("Playback"), "with or without a number");
        T.Ok(!RuleBook.IsPracticeAccountName("Sim103"),
             "a sim account is NOT practice - it is traded in real time and cannot be rewound");
        T.Ok(!RuleBook.IsPracticeAccountName("APEX-11325-106"), "and a funded account plainly is not");
        T.Ok(!RuleBook.IsPracticeAccountName(""), "nor is nothing at all");

        // The shape of the accident: same date, two worlds.
        PracticeBook book = new PracticeBook();
        PracticeRun r = book.RunFor("Playback101", Session, RealNow);
        r.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));

        T.Eq(r.SessionDate, Session.Date, "the run is filed under the day being replayed");
        T.Eq(r.RunNumber, 1, "as the first attempt at it");
        T.Eq(book.Count, 1, "and it is in the practice book");
    }

    static void ARewindStartsAFreshAttempt()
    {
        T.S("a rewind starts a fresh attempt");

        PracticeBook book = new PracticeBook();

        PracticeRun first = book.RunFor("Playback101", Session, RealNow);
        first.Trades.Add(Tr(1, -1, false, "Trade", ""));
        first.Trades.Add(Tr(2, -1, false, "Trade", ""));

        // Time moving forward is the same pass.
        T.Ok(book.RunFor("Playback101", Session.AddMinutes(40), RealNow) == first,
             "carrying on through the session is still the first pass");

        // A small step back is a scrub, not a restart. Splitting one pass into
        // two here would be the worse error of the two: a bad session would read
        // as several short tidy ones.
        T.Ok(book.RunFor("Playback101", Session.AddMinutes(12), RealNow) == first,
             "nudging the slider back to re-watch a move does not invent a second attempt");

        // Back to the open is a rewind.
        PracticeRun second = book.RunFor("Playback101", Session, RealNow.AddMinutes(30));
        T.Ok(second != first, "going back to the start is a new attempt");
        T.Eq(second.RunNumber, 2, "and it knows it is the second");

        // A different session is its own thing entirely.
        second.Trades.Add(Tr(1, -1, false, "Trade", ""));
        PracticeRun other = book.RunFor("Playback101", Session.AddDays(-3), RealNow.AddHours(1));
        T.Eq(other.RunNumber, 1, "a different day starts its own count");
        T.Eq(book.RunsFor("Playback101", Session).Count, 2, "two attempts at the sixth");
        T.Eq(book.RunsFor("Playback101", Session.AddDays(-3)).Count, 1, "one at the third");
    }

    static void TheScoreIsBehaviourNeverMoney()
    {
        T.S("the score is behaviour, never money");

        PracticeRun r = new PracticeRun();
        r.SessionDate = Session.Date;

        r.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));   // clean
        r.Trades.Add(Tr(2, 3, true, "Cooldown", BallastJournal.Verdict_Chased));     // three at once
        r.Trades.Add(Tr(3, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));   // clean
        r.Trades.Add(Tr(6, -1, false, "StopForDay", BallastJournal.Verdict_ByTheBook));

        PracticeScore s = PracticeBook.Score(r, 5, 15);

        T.Eq(s.Trades, 4, "four trades taken");
        T.Eq(s.InsideCooldown, 1, "one inside the cooldown");
        T.Eq(s.AfterAStopSignal, 2, "two after Ballast had already said something");
        T.Eq(s.OffPlan, 1, "one he called chased himself");
        T.Eq(s.PastTheCount, 1, "and one past the count of five");
        T.Eq(s.Clean, 2, "leaving two that broke nothing");

        // Every trade above lost $250. None of that is anywhere in the score,
        // and it must not be: replay fills are modelled and the session can be
        // run until it works, so money here is not information.
        T.Near(s.Adherence, 0.5, 0.001, "half of them were by his own rules");

        // A bot's trades and reconstructed rows are not his discipline.
        PracticeRun mixed = new PracticeRun();
        BallastTrade bot = Tr(1, -1, false, "StopForDay", BallastJournal.Verdict_Chased);
        bot.Automated = true;
        mixed.Trades.Add(bot);
        BallastTrade gap = Tr(2, -1, false, "", "");
        gap.MaxContracts = 0;
        mixed.Trades.Add(gap);
        mixed.Trades.Add(Tr(3, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));

        PracticeScore m = PracticeBook.Score(mixed, 5, 15);
        T.Eq(m.Trades, 1, "only the one he took by hand counts");
        T.Eq(m.Clean, 1, "and it was clean");
    }

    /// <summary>
    /// The score has to be believed on the day it says he improved. Counting an
    /// untagged trade as a good one would make every score drift upward as he
    /// tags less - which is exactly when he is least disciplined.
    /// </summary>
    static void SilenceIsNotCountedAsAGoodTrade()
    {
        T.S("an untagged trade is not counted as a good one");

        PracticeRun r = new PracticeRun();
        r.Trades.Add(Tr(1, -1, false, "Trade", ""));       // no verdict given
        r.Trades.Add(Tr(2, -1, false, "Trade", ""));

        PracticeScore s = PracticeBook.Score(r, 5, 15);
        T.Eq(s.OffPlan, 0, "silence is not an admission");
        T.Eq(s.Clean, 2, "and it is not held against him either - nothing else was broken");

        // But a trade he DID answer, badly, counts.
        r.Trades.Add(Tr(3, -1, false, "Trade", BallastJournal.Verdict_OffPlan));
        s = PracticeBook.Score(r, 5, 15);
        T.Eq(s.OffPlan, 1, "an answer he gave is taken at face value");
        T.Eq(s.Clean, 2, "and that trade is not clean");
    }

    /// <summary>
    /// The sentence the whole file exists for. Live, a better week might just be
    /// a better week. Here the market is held still.
    /// </summary>
    static void TheSameSessionTwiceIsAControlledTest()
    {
        T.S("the same session twice is a controlled test");

        PracticeRun first = new PracticeRun();
        first.SessionDate = Session.Date;
        first.RunNumber = 1;
        first.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));
        first.Trades.Add(Tr(2, 2, true, "Cooldown", BallastJournal.Verdict_Chased));
        first.Trades.Add(Tr(3, 4, true, "Cooldown", BallastJournal.Verdict_Chased));
        first.Trades.Add(Tr(4, 3, true, "Cooldown", BallastJournal.Verdict_Chased));
        first.Trades.Add(Tr(5, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));
        first.Trades.Add(Tr(6, -1, false, "StopForDay", BallastJournal.Verdict_Chased));

        PracticeRun second = new PracticeRun();
        second.SessionDate = Session.Date;
        second.RunNumber = 2;
        second.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));
        second.Trades.Add(Tr(2, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));
        second.Trades.Add(Tr(3, 40, true, "Trade", BallastJournal.Verdict_ByTheBook));

        string s = PracticeBook.Compare(first, second, 5, 15);

        T.Ok(s.IndexOf("run 1 against run 2") >= 0, "it says which passes: " + s);
        T.Ok(s.IndexOf("identical") >= 0,
             "and why the comparison is worth anything - the bars were the same");
        T.Ok(s.IndexOf("6 to 3") >= 0, "the trade counts");
        T.Ok(s.IndexOf("Inside your cooldown: 3 to 0") >= 0, "and what actually moved: " + s);
        T.Ok(s.IndexOf("by your own rules the second time") >= 0,
             "a clean run is named as one");

        // Money is nowhere in it. Every trade in both runs lost the same $250.
        T.Ok(s.IndexOf("$") < 0, "and there is not a dollar figure anywhere in it: " + s);

        // Backwards is reported as honestly as forwards.
        string worse = PracticeBook.Compare(second, first, 5, 15);
        T.Ok(worse.IndexOf("Worse") >= 0,
             "a second run that went backwards is told so: " + worse);
    }

    static void ItWillNotCallOneTradeProgress()
    {
        T.S("one trade different is not progress");

        PracticeRun a = new PracticeRun();
        a.SessionDate = Session.Date; a.RunNumber = 1;
        for (int n = 1; n <= 8; n++)
            a.Trades.Add(Tr(n, n > 4 ? 3 : -1, n > 4,
                            n > 4 ? "Cooldown" : "Trade", BallastJournal.Verdict_ByTheBook));

        PracticeRun b = new PracticeRun();
        b.SessionDate = Session.Date; b.RunNumber = 2;
        for (int n = 1; n <= 8; n++)
            b.Trades.Add(Tr(n, n > 5 ? 3 : -1, n > 5,
                            n > 5 ? "Cooldown" : "Trade", BallastJournal.Verdict_ByTheBook));

        string s = PracticeBook.Compare(a, b, 12, 15);
        T.Ok(s.IndexOf("No real change") >= 0,
             "one fewer breach out of eight is noise, and it says so: " + s);
        T.Ok(s.IndexOf("pick ONE thing") >= 0,
             "with the only instruction that helps on a replay");
    }

    static void NothingToCompareSaysNothing()
    {
        T.S("nothing to compare says nothing");

        PracticeRun empty = new PracticeRun();
        empty.SessionDate = Session.Date; empty.RunNumber = 2;

        PracticeRun real = new PracticeRun();
        real.SessionDate = Session.Date; real.RunNumber = 1;
        real.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));

        T.Eq(PracticeBook.Compare(real, empty, 5, 15), "",
             "a pass with no trades in it is not a result");
        T.Eq(PracticeBook.Compare(null, real, 5, 15), "", "and neither is nothing at all");

        PracticeRun otherDay = new PracticeRun();
        otherDay.SessionDate = Session.Date.AddDays(-1); otherDay.RunNumber = 1;
        otherDay.Trades.Add(Tr(1, -1, false, "Trade", BallastJournal.Verdict_ByTheBook));

        T.Eq(PracticeBook.Compare(otherDay, real, 5, 15), "",
             "two different mornings are not a controlled test - that is the whole point");

        PracticeScore none = PracticeBook.Score(new PracticeRun(), 5, 15);
        T.Near(none.Adherence, -1, 0.001,
               "no trades is the absence of a score, not a score of zero");
    }
}
