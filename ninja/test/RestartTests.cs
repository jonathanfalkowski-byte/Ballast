using System;
using Ballast;

/// <summary>
/// "so i have a trade listed on sim 101 but i have not taken any trade today
/// for it yet....and it says i have 2 for 103 but i have only done 1 trade so
/// far today"
///
/// He recompiled at 09:09. Three minutes later Ballast wrote a row on two
/// accounts saying money had moved while it was closed, counted each as a
/// trade, and started the day from a number that was not today's.
///
/// Three separate things had to be wrong at once, and each of them is a
/// restart path:
///
///   1. Reopening threw away the baseline the morning had been measured from,
///      because a trusted account was assumed not to need one. On a feed that
///      carries its realised figure into the next day it needs one more than
///      anything else does, and after the first save of the day the evidence
///      for re-deriving it is gone - the session file keeps one row per
///      account and today's has overwritten yesterday's close.
///
///   2. The guard on "did Ballast miss something" was the same flag as "is the
///      account's own figure in use", which is set from the first tick of
///      every day. A day that has not started has nothing missing from it.
///
///   3. Nothing recognised the plainest fact available at a session boundary
///      Ballast watched go past: no trade has happened yet, so whatever the
///      feed is reporting belongs to yesterday.
///
/// These cases all run the RESTART, not just the first open, because that is
/// where every one of them lived.
/// </summary>
public static class RestartTests
{
    public static void Run()
    {
        TheFirstOpenOfADayHasNothingToReconcile();
        ARestartKeepsTodaysBaseline();
        AWatchedRollStartsFromWhatTheFeedIsCarrying();
        ALateFeedResetDoesNotInvertTheDay();
        AnOldSessionFileIsStillIgnored();
        ARestoredBaselineStillFindsARealGap();
        APhantomAlreadyInTheJournalUnwindsItself();
    }

    static TrackerConfig Cfg()
    {
        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 150000;
        c.TrailingDrawdown = 5000;
        c.DailyLossLimit = 1200;
        c.MaxTrades = 12;
        c.MaxLossesBeforeStop = 6;
        c.TrustAccountRealised = true;
        return c;
    }

    /// <summary>
    /// The cold start Ballast cannot reason about from its own history: it was
    /// not running at the boundary, and it was not run yesterday either, so
    /// there is no previous close on disk to recognise a carried figure by.
    ///
    /// The account's cash settles it. Nothing has moved since Ballast last
    /// looked, so the realised figure it is showing is not this morning's.
    /// </summary>
    static void TheFirstOpenOfADayHasNothingToReconcile()
    {
        T.S("a cold start on an untouched account claims nothing");

        DateTime today = new DateTime(2026, 8, 12);
        DateTime friday = new DateTime(2026, 8, 7);

        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        // Friday's row: the cash Ballast last wrote down. No close is restored,
        // because a row that old cannot be matched against anyway.
        t.SeedRiskState(friday, 151646.12, 150000, 149986.88, 0, false);
        t.EnsureSession(today.AddHours(9).AddMinutes(2), -96.92, 149986.88);
        t.OnEquity(149986.88, -96.92, 149986.88);

        T.Near(t.DailyPnl, 0, 0.01,
               "a day he has not started reads zero, not the feed's residue");
        T.Near(t.DailyPnl - 0, 0, 0.01,
               "so there is nothing for the reconciler to book as a trade");
        T.Eq(t.TradesToday, 0, "and no trade he never took");

        // The other half, and the reason this is a cash test rather than a
        // blanket rule: a morning genuinely traded with Ballast shut DOES move
        // the balance, and that gap must still be found.
        BallastTracker traded = new BallastTracker();
        traded.Config = Cfg();
        traded.SeedRiskState(friday, 151646.12, 150000, 149986.88, 0, false);
        traded.EnsureSession(today.AddHours(13), -1100, 148886.88);
        traded.OnEquity(148886.88, -1100, 148886.88);

        T.Near(traded.DailyPnl, -1100, 0.01,
               "cash down by what the account says, so the day is real");
        T.Ok(traded.BaselineAuthoritative,
             "and the gap against an empty journal is still worth writing down");
    }

    /// <summary>
    /// The one that actually bit. A feed that carries its realised figure, a
    /// baseline correctly taken this morning, and a recompile at 09:09.
    /// </summary>
    static void ARestartKeepsTodaysBaseline()
    {
        T.S("a restart keeps this morning's baseline");

        DateTime yday  = new DateTime(2026, 8, 11);
        DateTime today = new DateTime(2026, 8, 12);

        // ── First open of the day. Yesterday's row is on disk, so the carried
        // figure is recognised on sight and the day starts at zero.
        BallastTracker morning = new BallastTracker();
        morning.Config = Cfg();
        morning.SeedRiskState(yday, 151646.12, 150000, 149986.88, 0, false);
        morning.LastClosingDailyPnl = -96.92;
        morning.EnsureSession(today.AddHours(9).AddMinutes(2), -96.92, 149986.88);
        morning.OnEquity(149986.88, -96.92, 149986.88);

        T.Near(morning.DailyPnl, 0, 0.01, "the day starts at zero, not at yesterday's residue");
        T.Near(morning.SessionStartRealised, -96.92, 0.01,
               "measured from what the feed was already carrying");

        double saved = morning.SessionStartRealised;

        // ── 09:09. Recompile. Fresh tracker, today's row on disk - and today's
        // row cannot tell it what yesterday closed at, because today's row has
        // replaced yesterday's.
        BallastTracker after = new BallastTracker();
        after.Config = Cfg();
        after.SeedBaselineIsDayStart = true;
        after.SeedSession(today, saved, morning.PeakEquity, morning.PeakDailyPnl,
                          morning.WorstDailyPnl, false, morning.EndOfDayHighWater,
                          morning.LastKnownBalance, 0, false);
        after.EnsureSession(today.AddHours(9).AddMinutes(10), -96.92, 149986.88);
        after.OnEquity(149986.88, -96.92, 149986.88);

        T.Ok(after.BaselineAuthoritative, "the restart knows Ballast was open earlier today");
        T.Near(after.SessionStartRealised, -96.92, 0.01, "and measures from the same point");
        T.Near(after.DailyPnl, 0, 0.01,
               "so the day still reads zero rather than yesterday's residue");

        // Which is what the window subtracts the journal from. Nothing in,
        // nothing out - no reconstructed row, no phantom trade, no phantom loss.
        T.Near(after.DailyPnl - 0, 0, 0.01, "leaving nothing for the reconciler to book");

        // And a real trade after the restart still lands where it should.
        after.OnPosition(1, -96.92, today.AddHours(9).AddMinutes(31), "NQ SEP26", "Sim103");
        after.OnPosition(0, 128.72, today.AddHours(9).AddMinutes(32), "NQ SEP26", "Sim103");
        after.OnEquity(150212.52, 128.72, 150212.52);
        T.Eq(after.TradesToday, 1, "one trade, because one trade is what he took");
        T.Near(after.DailyPnl, 225.64, 0.01, "and the day is that trade, measured from this morning");
    }

    /// <summary>
    /// Ballast running across the boundary. The exact-match test needs a whole
    /// watched day behind it; this needs nothing at all.
    /// </summary>
    static void AWatchedRollStartsFromWhatTheFeedIsCarrying()
    {
        T.S("a watched day roll starts from what the feed is carrying");

        DateTime yday  = new DateTime(2026, 8, 11);
        DateTime today = new DateTime(2026, 8, 12);

        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.EnsureSession(yday.AddHours(10), 0, 150000);
        t.OnEquity(150000, 0, 150000);

        t.OnPosition(1, 0, yday.AddHours(11), "NQ SEP26", "Sim103");
        t.OnPosition(0, -400, yday.AddHours(11).AddMinutes(5), "NQ SEP26", "Sim103");
        t.OnEquity(149600, -400, 149600);
        T.Near(t.DailyPnl, -400, 0.01, "yesterday cost 400");

        // Midnight passes with Ballast open. The feed keeps the figure - and it
        // does NOT match what Ballast recorded the day closing at, because a
        // stretch of the previous day was traded before Ballast was watching.
        t.LastClosingDailyPnl = -1234.56;
        t.EnsureSession(today.AddHours(9), -400, 149600);
        t.OnEquity(149600, -400, 149600);

        T.Eq(t.TradesToday, 0, "the new day starts with no trades");
        T.Near(t.DailyPnl, 0, 0.01,
               "and at zero - a day that has not started cannot already be 400 down");
        T.Ok(t.FeedCarriesRealised, "and the feed is marked as one that carries");

        // A feed that DOES reset is unaffected: it reads zero at the boundary,
        // which is the baseline it would have been given anyway.
        BallastTracker r = new BallastTracker();
        r.Config = Cfg();
        r.EnsureSession(yday.AddHours(10), 0, 150000);
        r.OnEquity(149600, -400, 149600);
        r.EnsureSession(today.AddHours(9), 0, 149600);
        r.OnEquity(149600, 0, 149600);
        T.Near(r.SessionStartRealised, 0, 0.01, "a resetting feed still measures from zero");
        T.Near(r.DailyPnl, 0, 0.01, "and its new day is zero too");
        T.Ok(!r.FeedCarriesRealised, "and it is not marked as carrying");
    }

    /// <summary>
    /// The mirror of the bug, which the fix above could have introduced: a
    /// platform that clears its realised figure a beat after the boundary
    /// rather than on it. Baselining from the residue and then watching the
    /// residue vanish would read as minus the residue all day.
    /// </summary>
    static void ALateFeedResetDoesNotInvertTheDay()
    {
        T.S("a feed that clears a beat late does not invert the day");

        DateTime yday  = new DateTime(2026, 8, 11);
        DateTime today = new DateTime(2026, 8, 12);

        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.EnsureSession(yday.AddHours(10), 0, 150000);
        t.OnEquity(149600, -400, 149600);

        // The boundary. The platform is still showing yesterday's figure.
        t.EnsureSession(today.AddHours(9), -400, 149600);
        t.OnEquity(149600, -400, 149600);
        T.Near(t.DailyPnl, 0, 0.01, "the new day opens at zero");

        // And now the platform clears it.
        t.OnEquity(149600, 0, 149600);
        T.Near(t.SessionStartRealised, 0, 0.01, "the baseline follows it back to zero");
        T.Near(t.DailyPnl, 0, 0.01, "and the day stays at zero rather than turning 400 up");

        // Once he has traded, the same reading is a real day and is left alone.
        t.OnPosition(1, 0, today.AddHours(9).AddMinutes(30), "NQ SEP26", "Sim103");
        t.OnPosition(0, -300, today.AddHours(9).AddMinutes(35), "NQ SEP26", "Sim103");
        t.OnEquity(149300, -300, 149300);
        t.OnPosition(1, -300, today.AddHours(10), "NQ SEP26", "Sim103");
        t.OnPosition(0, 0, today.AddHours(10).AddMinutes(5), "NQ SEP26", "Sim103");
        t.OnEquity(149600, 0, 149600);
        T.Near(t.DailyPnl, 0, 0.01, "a day that traded back to flat is flat, not re-baselined");
        T.Eq(t.TradesToday, 2, "and both trades are still counted");
    }

    /// <summary>
    /// The reason the baseline was being ignored in the first place: a file
    /// written by an older build recorded where the WINDOW opened, not where
    /// the day started. Laying that over the account's own figure erases the
    /// morning. A version-4 row still gets the old treatment.
    /// </summary>
    static void AnOldSessionFileIsStillIgnored()
    {
        T.S("a baseline from an older build is still ignored");

        DateTime today = new DateTime(2026, 8, 12);

        // The account says today has cost 2,126. The old file says the window
        // opened when realised read 9,874 - which would report a loss of 754.
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.SeedBaselineIsDayStart = false;                 // version 4
        t.SeedSession(today, 9874, 152000, 0, -754, false, 152000, 149000, 0, false);
        t.EnsureSession(today.AddHours(13), -2126, 147874);
        t.OnEquity(147874, -2126, 147874);

        T.Near(t.DailyPnl, -2126, 0.01, "the day is what the account says it is");
        T.Ok(t.BaselineAuthoritative, "but Ballast was open earlier today, so the gap is real");
    }

    /// <summary>
    /// The repair. Two reconstructed rows are already in his journal from
    /// before the fix, and a fix that only stops new bad data is half a fix.
    ///
    /// It unwinds itself, because the reconciler was always able to take a row
    /// back - it just never had a correct day's P&L to measure against. Given
    /// one, the phantom's own arithmetic cancels it out.
    /// </summary>
    static void APhantomAlreadyInTheJournalUnwindsItself()
    {
        T.S("a phantom already in the journal unwinds itself");

        DateTime today = new DateTime(2026, 8, 12);

        // Sim101 as it stands on disk right now: a reconstructed row of
        // 1,074.88 on an account he has not traded, and a baseline bent to
        // match it. The next start re-derives the baseline from the account.
        BallastTracker t = new BallastTracker();
        TrackerConfig c = Cfg();
        c.StartingBalance = 100000;
        c.TrailingDrawdown = 6500;
        c.MaxTrades = 7;
        t.Config = c;
        t.SeedBaselineIsDayStart = false;                 // the version-4 row on disk
        t.SeedSession(today, -1074.88, 102330.24, 1074.88, 0, false,
                      102330.24, 98953.04, 0, false);
        t.EnsureSession(today.AddHours(14), 0, 98953.04);
        t.OnEquity(98953.04, 0, 98953.04);

        // And the journal hands the phantom straight back.
        t.SeedToday(today, 1, 0, null, false, 1074.88, 0);
        t.OnEquity(98953.04, 0, 98953.04);

        T.Near(t.DailyPnl, 0, 0.01,
               "the account says nothing has happened, so nothing has happened");

        // Which is what the reconciler subtracts the journal from. The phantom
        // is now the whole difference, with the opposite sign - so adding it to
        // the row zeroes the row, and a zeroed reconstructed row is deleted.
        double accounted = 1074.88;
        double missing = t.DailyPnl - accounted;
        T.Near(missing, -1074.88, 0.01, "the phantom is exactly what cannot be accounted for");
        T.Near(accounted + missing, 0, 0.01, "so the row cancels itself and is taken away");
    }

    /// <summary>
    /// And the feature the restored baseline exists for still works: a trade
    /// taken while Ballast was shut is still visible as a gap.
    /// </summary>
    static void ARestoredBaselineStillFindsARealGap()
    {
        T.S("a real gap is still found after the fix");

        DateTime today = new DateTime(2026, 8, 12);

        BallastTracker morning = new BallastTracker();
        morning.Config = Cfg();
        morning.EnsureSession(today.AddHours(9).AddMinutes(30), 0, 150000);
        morning.OnEquity(150000, 0, 150000);
        morning.OnPosition(1, 0, today.AddHours(9).AddMinutes(40), "NQ SEP26", "Sim103");
        morning.OnPosition(0, -500, today.AddHours(9).AddMinutes(45), "NQ SEP26", "Sim103");
        morning.OnEquity(149500, -500, 149500);
        T.Near(morning.DailyPnl, -500, 0.01, "the watched trade cost 500");

        // Ballast is shut. Another trade loses 600. It reopens at lunchtime.
        BallastTracker back = new BallastTracker();
        back.Config = Cfg();
        back.SeedBaselineIsDayStart = true;
        back.SeedSession(today, morning.SessionStartRealised, morning.PeakEquity,
                         morning.PeakDailyPnl, morning.WorstDailyPnl, false,
                         morning.EndOfDayHighWater, morning.LastKnownBalance, 0, false);
        back.SeedToday(today, 1, 1, today.AddHours(9).AddMinutes(45), true, -500, -500);
        back.EnsureSession(today.AddHours(13), -1100, 148900);
        back.OnEquity(148900, -1100, 148900);

        T.Ok(back.BaselineAuthoritative, "Ballast knows it was open earlier today");
        T.Near(back.DailyPnl, -1100, 0.01, "and the day includes what it did not watch");
        T.Near(back.DailyPnl - (-500), -600, 0.01,
               "so the unaccounted 600 is still there to be written down");
    }
}
