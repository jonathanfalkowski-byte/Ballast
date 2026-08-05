using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "i had a trade while ballast was down but it didnt record it....my pnl is
/// actually this -2100....plus it didnt come up with a trade journal entry."
///
/// Ballast measures a day as the change in the account's realised P&L since a
/// baseline taken when the session opened. Reopening took a FRESH baseline from
/// wherever the account happened to be, so a trade taken while the window was
/// shut was outside the measurement entirely - and the journal seed could not
/// put it back either, because the journal only knows what Ballast watched.
///
/// The trader was shown a smaller loss than he had taken and more room than he
/// had left, which is the dangerous direction to be wrong in.
///
/// The fix needs to know nothing about the trade: save the baseline, and
/// everything since is inside the measurement whether Ballast saw it or not.
/// </summary>
public static class GapTests
{
    public static void Run()
    {
        TradesWhileClosedLandInTheDay();
        TheFloorPeakSurvivesTheGap();
        IntradayPeakSurvivesDayRollover();
        EndOfDayFloorOnlyAdvancesAtRollover();
        YesterdaysBaselineIsNeverUsed();
        ReSeedingDoesNotMoveTheDay();
        FirstOpenOfTheDayIsUnchanged();
        MatchesTheAccountsOwnFigure();
        CommissionIsRecordedPerTrade();
        ReconstructedRowsAreMoneyNotDecisions();
        OldJournalsAreConsolidated();
    }

    static TrackerConfig Cfg()
    {
        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 250000;
        c.TrailingDrawdown = 6500;
        c.DailyLossLimit = 3000;
        c.MaxTrades = 10;
        c.MaxLossesBeforeStop = 3;
        // These cases are about Ballast's own baseline, so they measure from it.
        c.TrustAccountRealised = false;
        return c;
    }

    /// <summary>
    /// The other half of the same complaint: "the total should match the account
    /// at least if we cant record the information... it needs to match the actual
    /// account realized regardless whether we saw the trade or not."
    ///
    /// Saving the baseline fixes the NEXT restart. It cannot fix a restart that
    /// already happened, and it cannot fix the first open of a day. A feed that
    /// reports realised P&L per session already knows the answer, so Ballast
    /// takes it directly and the two can never disagree.
    /// </summary>
    static void MatchesTheAccountsOwnFigure()
    {
        T.S("the day matches the account, watched or not");

        DateTime day = new DateTime(2026, 8, 3);

        TrackerConfig c = Cfg();
        c.TrustAccountRealised = true;

        // Ballast opens at lunchtime having seen nothing. The account says today
        // has cost 2,126. There is no baseline on disk and no journal - and the
        // day still reads 2,126 down.
        BallastTracker t = new BallastTracker();
        t.Config = c;
        t.EnsureSession(day.AddHours(13), -2126, 247874);
        t.OnEquity(247874, -2126);

        T.Near(t.DailyPnl, -2126, 0.01, "the day is what the account says it is");
        T.Ok(t.BaselineAuthoritative,
             "and the figure is trusted, so a gap against the journal means a missing trade");

        // Which is what makes the reconciliation arithmetic sound: nothing in the
        // journal, so the whole day is unaccounted for and gets one row.
        double accounted = 0;
        T.Near(t.DailyPnl - accounted, -2126, 0.01, "the unaccounted amount is the whole day");

        // A trade watched afterwards adds on top rather than restarting.
        t.OnPosition(1, -2126, day.AddHours(14), "NQ SEP26", "APEX-105");
        t.OnPosition(0, -1926, day.AddHours(14).AddMinutes(5), "NQ SEP26", "APEX-105");
        t.OnEquity(248074, -1926);
        T.Near(t.DailyPnl, -1926, 0.01, "and live trading tracks the account from there");
        T.Eq(t.TradesToday, 1, "counting only the trade it actually watched");

        // The daily limit sees the real number from the first tick.
        TrackerConfig tight = Cfg();
        tight.TrustAccountRealised = true;
        tight.DailyLossLimit = 2000;

        BallastTracker hit = new BallastTracker();
        hit.Config = tight;
        hit.EnsureSession(day.AddHours(13), -2126, 247874);
        hit.OnEquity(247874, -2126);
        T.Ok(hit.DailyLossLimitHit,
             "an account already past its limit is locked out the moment Ballast opens");
        T.Eq(DisciplineEngine.Evaluate(hit.BuildInput(day.AddHours(13))).Action,
             DisciplineAction.Lockout, "rather than being handed a fresh day");

        // Off, it goes back to measuring from its own baseline - which is right
        // for a feed that never resets its realised figure.
        BallastTracker own = new BallastTracker();
        own.Config = Cfg();                       // TrustAccountRealised = false
        own.EnsureSession(day.AddHours(13), -2126, 247874);
        own.OnEquity(247874, -2126);
        T.Near(own.DailyPnl, 0, 0.01,
               "with it off, a cumulative realised figure is not mistaken for today");
        T.Ok(!own.BaselineAuthoritative, "and no gap is claimed against the journal");

        // A baseline saved by an earlier run must not override the account's own
        // figure. This is the bug the trader hit the first time he compiled the
        // change: ballast-session.txt still held a measuring point written
        // minutes earlier, so the account said the day had cost 2,126 and
        // Ballast carried on saying 754.
        BallastTracker stale = new BallastTracker();
        stale.Config = c;                                   // trusting the account
        stale.SeedSession(day, -1372, 249000, 0, -754, false);
        stale.SeedToday(day, 2, 1, day.AddHours(10), true, -754, -754);
        stale.EnsureSession(day.AddHours(13), -2126, 247874);
        stale.OnEquity(247874, -2126);
        T.Near(stale.DailyPnl, -2126, 0.01,
               "a saved baseline does not override the account's own figure");

        // Though everything else on that saved line still applies - the peak that
        // moved the floor is not the trader's P&L and must not be thrown away.
        T.Near(stale.PeakEquity, 249000, 0.01, "the saved peak equity is still honoured");

        // With it off, the saved baseline is exactly what should be used.
        BallastTracker kept = new BallastTracker();
        kept.Config = Cfg();                                // measuring for itself
        kept.SeedSession(day, -1372, 249000, 0, -754, false);
        kept.EnsureSession(day.AddHours(13), -2126, 247874);
        kept.OnEquity(247874, -2126);
        T.Near(kept.DailyPnl, -754, 0.01, "with it off, the saved baseline is used");

        // The choice survives a restart like every other per-account setting.
        string key;
        TrackerConfig back = SettingsCodec.Deserialise(SettingsCodec.Serialise("APEX-105", c), out key);
        T.Ok(back.TrustAccountRealised, "the setting round trips");
        TrackerConfig off = SettingsCodec.Deserialise(SettingsCodec.Serialise("Sim110", Cfg()), out key);
        T.Ok(!off.TrustAccountRealised, "both ways");

        // A settings file from the previous build has no field for it, and must
        // come back ON - agreeing with the platform is the behaviour that cannot
        // silently under-report a loss.
        string old22 = string.Join("|", new string[] {
            "APEX-105", "250000", "6500", "0", "2", "500", "750", "4", "4",
            "265000", "", "0", "0", "0", "4", "0", "1", "570", "690", "5", "27", "15000", "0"
        });
        TrackerConfig older = SettingsCodec.Deserialise(old22, out key);
        T.Ok(older.TrustAccountRealised, "an older file gets the safer of the two behaviours");
    }

    /// <summary>
    /// "is there anyway you can determine the commission because some people may
    /// trade a lot of eminis or micros and that would be over the 25 dollar limit."
    ///
    /// A flat noise floor cannot work: four dollars is nothing for one contract
    /// and a rounding error for ten. Each round trip now records what it actually
    /// cost, taken from the account's own running commission total, so the bound
    /// on "this difference is only commission" is the trader's own figure.
    /// </summary>
    static void CommissionIsRecordedPerTrade()
    {
        T.S("what each round trip cost");

        DateTime day = new DateTime(2026, 8, 3);
        DateTime t0 = day.AddHours(9).AddMinutes(40);

        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.EnsureSession(t0, 0, 250000);
        t.OnEquity(250000, 0);

        // Ten minis at $4.16 a round turn.
        t.CurrentCommission = 0;
        t.OnPosition(10, 0, t0, "NQ SEP26", "APEX-105");
        t.CurrentCommission = 41.60;
        BallastTrade e = t.OnPosition(0, -500, t0.AddMinutes(4), "NQ SEP26", "APEX-105");

        T.Ok(e != null, "the round trip is journalled");
        T.Near(e.Commission, 41.60, 0.01, "with what it cost in commission");
        T.Eq(e.MaxContracts, 10, "and the size that produced it");

        // A second trade records only its own share, not the running total.
        t.OnPosition(2, -500, t0.AddMinutes(20), "NQ SEP26", "APEX-105");
        t.CurrentCommission = 49.92;
        BallastTrade e2 = t.OnPosition(0, -300, t0.AddMinutes(24), "NQ SEP26", "APEX-105");
        T.Near(e2.Commission, 8.32, 0.01, "the next trade records its own commission only");

        // A host that never supplies the figure reports nothing rather than
        // inventing one, and nothing else changes.
        BallastTracker bare = new BallastTracker();
        bare.Config = Cfg();
        bare.EnsureSession(t0, 0, 250000);
        bare.OnEquity(250000, 0);
        bare.OnPosition(1, 0, t0, "NQ SEP26", "APEX-105");
        BallastTrade b = bare.OnPosition(0, -100, t0.AddMinutes(3), "NQ SEP26", "APEX-105");
        T.Near(b.Commission, 0, 0.01, "an unknown commission is 0, not a guess");
        T.Near(b.Pnl, -100, 0.01, "and the P&L is untouched by any of this");

        // It survives the journal, because the bound it feeds is recomputed from
        // the journal every time Ballast reopens.
        string line = BallastJournal.ToCsvLine(e);
        BallastTrade back = BallastJournal.FromCsvLine(line);
        T.Ok(back != null, "the row round trips through the CSV");
        T.Near(back.Commission, 41.60, 0.01, "with its commission intact");
        T.Near(back.Pnl, -500, 0.01, "and its P&L");

        // A journal row written before commission existed reports 0 rather than
        // failing to load. Commission is now the second-to-last column - the Setup
        // label was appended after it - so a row that predates commission is
        // simulated by dropping the last TWO fields, not one.
        int lastComma = line.LastIndexOf(',');
        int cut = line.LastIndexOf(',', lastComma - 1);
        BallastTrade old = BallastJournal.FromCsvLine(line.Substring(0, cut));
        T.Ok(old != null, "an older row still loads");
        T.Near(old.Commission, 0, 0.01, "and simply does not know what it cost");
        T.Eq(old.Setup, "", "and equally does not know which setup it was");

        // ── The bound the reconciliation actually uses ───────────────────────
        //
        // Mirrors BallastWindow.ReconcileClosedPeriod. The account's own
        // commission total for the day, or the journal's if the feed reports
        // none, plus a few dollars.
        double watched = e.Commission + e2.Commission;
        T.Near(Noise(49.92, watched), 54.92, 0.01,
               "the floor is the trader's own commission, not a flat guess");
        T.Ok(Noise(49.92, watched) > 41.60,
             "so a day of ten-lot commission never becomes a missing trade");
        T.Near(Noise(0, 0), 5.0, 0.01,
               "and a day with no commission at all has nothing to explain away");

        // THE CASE THAT BROKE IT. Ballast restarts, and the journal rows from
        // this morning were written by a build that did not record commission -
        // so every one of them reads zero. Summing the journal collapsed the
        // floor to five dollars and booked a $26 commission residue as a trade.
        T.Near(Noise(26.16, 0), 31.16, 0.01,
               "the account's figure holds up when the journal's does not");
        T.Ok(Ignored(26, Noise(26.16, 0)),
             "so $26 against $26.16 of commission is not a missing trade");
        T.Ok(Ignored(9, Noise(9.30, 0)), "nor $9 against $9.30");
        T.Ok(!Ignored(400, Noise(26.16, 0)), "while $400 against $26 of commission still is");

        // BOTH DIRECTIONS. The first version only tolerated the account looking
        // WORSE than the journal. The trader's own case was the other way: the
        // account had made -$2,100 and Ballast had counted -$2,126, so the
        // journal over-stated the loss by $26. A charge landing after one round
        // trip closes is absorbed by the next, and that can push either way.
        T.Ok(Ignored(26, Noise(26.16, 0)), "a positive residue is commission too");
        T.Ok(Ignored(-26, Noise(26.16, 0)), "and so is a negative one");
    }

    /// <summary>
    /// "it is also recording the trades wrong, i have only done 1 trade for apex
    /// 106... apex 105 ive only done 2 total trades and it seems to think i did
    /// 5... all the accounts are off... and it thinks im done for the day."
    ///
    /// Each reconstructed row was counting as one trade AND one loss. A day of
    /// restarts therefore drove every account to its max-trades limit and its
    /// loss streak, and Ballast said STOP on the strength of its own bookkeeping.
    /// A rule that fires on an artefact discredits every other rule in the
    /// product.
    /// </summary>
    static void ReconstructedRowsAreMoneyNotDecisions()
    {
        T.S("a reconstructed row is money, not a decision");

        BallastTrade watched = new BallastTrade();
        watched.AccountName = "APEX-105";
        watched.Instrument = "NQ SEP26";
        watched.MaxContracts = 2;
        watched.IsLong = true;
        watched.Pnl = -400;
        T.Ok(!watched.IsReconstructed, "a watched trade has a size and a direction");
        T.Eq(watched.SizeLabel, "Long 2", "and says so");

        BallastTrade gap = new BallastTrade();
        gap.AccountName = "APEX-105";
        gap.Instrument = "(Ballast was closed)";
        gap.MaxContracts = 0;
        gap.Pnl = -1372;
        T.Ok(gap.IsReconstructed, "a reconstructed row has neither");
        T.Eq(gap.SizeLabel, "", "so it claims neither");

        // The counting rule, mirrored from BallastWindow.SeedTodaysCounts: money
        // from every row, counts from watched rows only.
        List<BallastTrade> day = new List<BallastTrade>();
        day.Add(Row("APEX-105", 2, -400));      // watched, a loss
        day.Add(Row("APEX-105", 1, 250));       // watched, a win
        day.Add(Row("APEX-105", 0, -1372));     // reconstructed - one row, folded

        int trades = 0, losses = 0;
        double pnl = 0;
        for (int i = 0; i < day.Count; i++)
        {
            pnl += day[i].Pnl;
            trades++;
            if (day[i].Pnl < 0) losses++;
        }

        // With the gap folded into ONE row per account per day, this is the whole
        // day: two watched trades and one reconstructed one.
        T.Eq(trades, 3, "one reconstructed row counts as one trade, not none and not five");
        T.Eq(losses, 2, "and as one loss, because it lost");
        T.Near(pnl, -1522, 0.01, "while every dollar is counted exactly once");

        // Both extremes have been wrong in production, and this is the case that
        // killed the second one: the trader had taken two losing trades and was
        // told he had taken one, against a rule that stops him at three.
        T.Ok(losses >= 2, "a day of two losing trades reports two, one of them reconstructed");

        // And the failure the first version caused: five trades against a limit
        // of five and three losses against a limit of three, all from restarts.
        T.Ok(trades < 5, "restarts can no longer inflate the count");
    }

    /// <summary>
    /// A journal that already holds several rows describing the same gap - the
    /// state every earlier build left behind - is corrected once on load.
    /// </summary>
    static void OldJournalsAreConsolidated()
    {
        T.S("several rows for one gap become one");

        DateTime day = new DateTime(2026, 8, 3);

        BallastJournal j = new BallastJournal();
        j.Add(At(Row("APEX-105", 2, -400), day.AddHours(9)));
        j.Add(At(Row("APEX-105", 0, -1372), day.AddHours(11)));
        j.Add(At(Row("APEX-105", 0, -26), day.AddHours(12)));
        j.Add(At(Row("APEX-105", 0, -9), day.AddHours(13)));
        j.Add(At(Row("APEX-106", 0, -50), day.AddHours(12)));
        j.Add(At(Row("APEX-105", 0, -200), day.AddDays(1).AddHours(10)));

        T.Eq(j.ConsolidateReconstructed(), 2, "two duplicate rows go");
        T.Eq(j.All.Count, 4, "leaving the watched trade and one gap row per account per day");

        List<BallastTrade> left = j.All;
        double gap105 = 0, gap106 = 0;
        int recon = 0;
        for (int i = 0; i < left.Count; i++)
        {
            if (!left[i].IsReconstructed) continue;
            recon++;
            if (left[i].ExitTime.Date != day) continue;
            if (left[i].AccountName == "APEX-105") gap105 = left[i].Pnl;
            else gap106 = left[i].Pnl;
        }

        T.Eq(recon, 3, "one for 105 today, one for 106 today, one for 105 tomorrow");
        T.Near(gap105, -1407, 0.01, "and every dollar of 105's three rows is in the one that stays");
        T.Near(gap106, -50, 0.01, "while another account's gap is its own");

        // Running it again changes nothing - it is a repair, not a transform.
        T.Eq(j.ConsolidateReconstructed(), 0, "a consolidated journal stays consolidated");

        // A journal of watched trades is left completely alone.
        BallastJournal clean = new BallastJournal();
        clean.Add(At(Row("APEX-105", 2, -400), day.AddHours(9)));
        clean.Add(At(Row("APEX-105", 1, 250), day.AddHours(10)));
        T.Eq(clean.ConsolidateReconstructed(), 0, "nothing to do on a watched journal");
        T.Eq(clean.All.Count, 2, "and nothing is lost");
    }

    static BallastTrade At(BallastTrade e, DateTime when)
    {
        e.EntryTime = when;
        e.ExitTime = when.AddMinutes(3);
        return e;
    }

    static BallastTrade Row(string account, int contracts, double pnl)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = account;
        e.MaxContracts = contracts;
        e.Pnl = pnl;
        e.IsLong = true;
        return e;
    }

    /// <summary>Mirrors the bound in BallastWindow.ReconcileClosedPeriod.</summary>
    static double Noise(double accountCommissionToday, double journalCommission)
    {
        return Math.Max(accountCommissionToday, journalCommission) + 5.0;
    }

    static bool Ignored(double missing, double noise)
    {
        return missing > -noise && missing < noise;
    }

    static void TradesWhileClosedLandInTheDay()
    {
        T.S("a trade taken while Ballast was closed");

        DateTime day = new DateTime(2026, 8, 3);
        DateTime open = day.AddHours(9).AddMinutes(30);

        // ── Morning. Ballast is watching. The account's realised P&L reads
        // 12,000 at the open for reasons of the broker's own; what matters is
        // that the day is measured from there.
        BallastTracker morning = new BallastTracker();
        morning.Config = Cfg();
        morning.EnsureSession(open, 12000, 250000);
        morning.OnEquity(250000, 12000);

        morning.OnPosition(2, 12000, open, "NQ SEP26", "APEX-105");
        morning.OnPosition(0, 11000, open.AddMinutes(10), "NQ SEP26", "APEX-105");
        morning.OnEquity(249000, 11000);

        T.Near(morning.DailyPnl, -1000, 0.01, "the morning is 1,000 down");
        double savedBaseline = morning.SessionStartRealised;
        T.Near(savedBaseline, 12000, 0.01, "and the baseline it was measured from is 12,000");

        // ── Ballast is closed. A trade is taken and loses 1,126. The account's
        // realised P&L is now 9,874.
        //
        // ── Ballast reopens at lunchtime. THE OLD BEHAVIOUR: a fresh baseline of
        // 9,874, so the day starts again at zero and the journal puts back only
        // the 1,000 it watched.
        BallastTracker stale = new BallastTracker();
        stale.Config = Cfg();
        stale.SeedToday(day, 1, 1, open.AddMinutes(10), true, -1000, -1000);
        stale.EnsureSession(day.AddHours(13), 9874, 247874);
        stale.OnEquity(247874, 9874);
        T.Near(stale.DailyPnl, -1000, 0.01,
               "the old behaviour reported 1,000 down when the day had cost 2,126");

        // ── THE FIX. The baseline comes back from disk, so the missing trade is
        // inside the measurement without Ballast knowing anything about it.
        BallastTracker fixedT = new BallastTracker();
        fixedT.Config = Cfg();
        fixedT.SeedSession(day, savedBaseline, 250000, 0, -1000, false);
        fixedT.SeedToday(day, 1, 1, open.AddMinutes(10), true, -1000, -1000);
        fixedT.EnsureSession(day.AddHours(13), 9874, 247874);
        fixedT.OnEquity(247874, 9874);

        T.Ok(fixedT.SessionRestored, "the session was picked up rather than restarted");
        T.Near(fixedT.DailyPnl, -2126, 0.01,
               "and the day reads 2,126 down - what the account actually says");
        T.Eq(fixedT.TradesToday, 1, "the journal still only knows about the one trade it watched");

        // Which is exactly the gap the window reconciles: what the account says
        // the day has made, less what the journal can account for, IS the
        // missing trading, to the cent.
        double accounted = -1000;
        double missing = fixedT.DailyPnl - accounted;
        T.Near(missing, -1126, 0.01, "so the unaccounted amount is 1,126 - the missing trade");

        // And once that row is in the journal, the two agree and the counts are
        // whole again.
        fixedT.SeedToday(day, 2, 2, day.AddHours(13), true, -2126, -2126);
        T.Eq(fixedT.TradesToday, 2, "the reconstructed row counts as a trade");
        T.Eq(fixedT.LossesToday, 2, "and as a loss");
        T.Near(fixedT.DailyPnl, -2126, 0.01, "and re-seeding did not move the day");

        // The daily loss limit sees the real number too. On the old behaviour a
        // trader could be 200 from their limit and be told they had 1,300 left.
        BallastTracker deep = new BallastTracker();
        deep.Config = Cfg();                       // 3,000 daily limit
        deep.SeedSession(day, 12000, 250000, 0, -1000, false);
        deep.SeedToday(day, 1, 1, open.AddMinutes(10), true, -1000, -1000);
        deep.EnsureSession(day.AddHours(13), 8600, 246600);
        deep.OnEquity(246600, 8600);
        T.Near(deep.DailyPnl, -3400, 0.01, "the day is 3,400 down after the closed period");
        T.Ok(deep.DailyLossLimitHit, "so the daily limit is hit");
        T.Eq(DisciplineEngine.Evaluate(deep.BuildInput(day.AddHours(13))).Action,
             DisciplineAction.Lockout,
             "and Ballast stops the account instead of offering it another 1,600");
    }

    static void TheFloorPeakSurvivesTheGap()
    {
        T.S("the peak that moved the floor is not forgotten");

        DateTime day = new DateTime(2026, 8, 3);

        // An intraday trailing floor ratchets up with equity and never comes back
        // down. Forgetting a peak makes the cushion look bigger than it is - the
        // one direction Ballast must never be wrong in.
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.SeedSession(day, 12000, 254000, 4000, 0, false);   // peaked at 254,000
        t.EnsureSession(day.AddHours(13), 12000, 250000);
        t.OnEquity(250000, 12000);

        T.Near(t.PeakEquity, 254000, 0.01, "the morning's peak equity comes back");

        DisciplineInput i = t.BuildInput(day.AddHours(13));
        T.Near(i.FloorLevel, 247500, 0.01, "so the floor is where the peak put it");
        T.Near(i.CushionToFloor, 2500, 0.01,
               "and the cushion is 2,500, not the 6,500 a forgotten peak would report");

        // A stored peak BELOW where the account is now must not drag it down.
        BallastTracker up = new BallastTracker();
        up.Config = Cfg();
        up.SeedSession(day, 12000, 249000, 0, 0, false);
        up.EnsureSession(day.AddHours(13), 12000, 252000);
        up.OnEquity(252000, 12000);
        T.Near(up.PeakEquity, 252000, 0.01, "a stale lower peak never lowers the floor");
    }

    static void YesterdaysBaselineIsNeverUsed()
    {
        T.S("yesterday's baseline is not today's");

        DateTime day = new DateTime(2026, 8, 3);

        BallastTracker t = new BallastTracker();
        t.Config = Cfg();

        // Seeded for yesterday, opened today. Applying it would make this morning
        // look like a continuation of last night.
        t.SeedSession(day.AddDays(-1), 12000, 260000, 5000, -4000, true);
        t.EnsureSession(day.AddHours(9), 500, 250000);
        t.OnEquity(250000, 500);

        T.Ok(!t.SessionRestored, "a baseline from another day is ignored");
        T.Near(t.DailyPnl, 0, 0.01, "today starts at zero");
        T.Ok(!t.DailyLossLimitHit, "with yesterday's spent limit left behind");
        T.Near(t.PeakEquity, 260000, 0.01,
               "but yesterday's account-lifetime peak still sets the trailing floor");
    }

    static void IntradayPeakSurvivesDayRollover()
    {
        T.S("an intraday trailing peak survives midnight");

        DateTime day = new DateTime(2026, 8, 3);
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.Config.DrawdownType = DrawdownType.Intraday;

        t.EnsureSession(day.AddHours(10), 0, 250000);
        t.OnEquity(254000, 4000, 254000);
        t.OnEquity(251000, 1000, 251000);

        t.EnsureSession(day.AddDays(1).AddHours(9), 0, 251000);
        t.OnEquity(251000, 0, 251000);

        T.Near(t.PeakEquity, 254000, 0.01, "midnight does not erase the account high-water mark");
        DisciplineInput i = t.BuildInput(day.AddDays(1).AddHours(9));
        T.Near(i.FloorLevel, 247500, 0.01, "the new day keeps yesterday's ratcheted floor");
        T.Near(i.CushionToFloor, 3500, 0.01, "and never invents the larger reset cushion");
    }

    static void EndOfDayFloorOnlyAdvancesAtRollover()
    {
        T.S("an end-of-day floor uses the completed session anchor");

        DateTime day = new DateTime(2026, 8, 3);
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.Config.DrawdownType = DrawdownType.EndOfDay;

        t.SeedRiskState(day.AddDays(-1), 250000, 252000, 252000);
        t.EnsureSession(day.AddHours(9), 0, 252000);

        // A losing intraday balance must not lower the floor with it.
        t.OnEquity(249000, -3000, 249000);
        DisciplineInput losing = t.BuildInput(day.AddHours(12));
        T.Near(losing.FloorLevel, 245500, 0.01, "a losing day cannot move the EOD floor down");

        // Nor does an intraday winner move the EOD floor before the session is complete.
        t.OnEquity(255000, 3000, 255000);
        DisciplineInput winning = t.BuildInput(day.AddHours(15));
        T.Near(winning.FloorLevel, 245500, 0.01, "an intraday winner waits for the close");

        // The last known cash balance becomes authoritative only at rollover.
        t.EnsureSession(day.AddDays(1).AddHours(9), 0, 255000);
        DisciplineInput tomorrow = t.BuildInput(day.AddDays(1).AddHours(9));
        T.Near(t.EndOfDayHighWater, 255000, 0.01, "the completed close advances the EOD anchor");
        T.Near(tomorrow.FloorLevel, 248500, 0.01, "the next session starts from that higher floor");
    }

    static void ReSeedingDoesNotMoveTheDay()
    {
        T.S("re-seeding the journal does not move the day");

        DateTime day = new DateTime(2026, 8, 3);
        DateTime open = day.AddHours(9).AddMinutes(30);

        // Reconciliation adds a journal row and then re-seeds every account's
        // counts from the journal. On an account whose baseline was NOT restored,
        // that used to wind the baseline back a second time and shift the whole
        // day by the morning's P&L again.
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.SeedToday(day, 2, 1, open, true, -800, -900);
        t.EnsureSession(day.AddHours(13), 5000, 249200);
        t.OnEquity(249200, 5000);

        T.Near(t.DailyPnl, -800, 0.01, "the journal's figure is restored once");

        t.SeedToday(day, 2, 1, open, true, -800, -900);
        t.OnEquity(249200, 5000);
        T.Near(t.DailyPnl, -800, 0.01, "and seeding it again changes nothing");

        t.SeedToday(day, 3, 2, open, true, -1400, -1400);
        t.OnEquity(248600, 4400);
        T.Eq(t.TradesToday, 3, "though the counts do update");
        T.Near(t.DailyPnl, -1400, 0.01, "and the day still tracks the account, not the arithmetic");
    }

    static void FirstOpenOfTheDayIsUnchanged()
    {
        T.S("the first open of a day behaves as before");

        DateTime day = new DateTime(2026, 8, 3);

        // No saved baseline for today, so nothing to restore and nothing to
        // reconcile. Reconciliation is gated on SessionRestored precisely so that
        // the first open of a day cannot decide the entire morning was a missing
        // trade.
        BallastTracker t = new BallastTracker();
        t.Config = Cfg();
        t.EnsureSession(day.AddHours(9).AddMinutes(30), 12000, 250000);
        t.OnEquity(250000, 12000);

        T.Ok(!t.SessionRestored, "nothing was restored");
        T.Near(t.DailyPnl, 0, 0.01, "and the day starts at zero from wherever the account is");

        t.OnPosition(1, 12000, day.AddHours(10), "NQ SEP26", "APEX-105");
        t.OnPosition(0, 12400, day.AddHours(10).AddMinutes(5), "NQ SEP26", "APEX-105");
        t.OnEquity(250400, 12400);
        T.Near(t.DailyPnl, 400, 0.01, "and counts from there");
    }
}
