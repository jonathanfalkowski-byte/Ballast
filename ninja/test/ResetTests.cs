using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "ok i reset sim103 but it doesnt recogonize i reset it.... how do we make it
/// recogonize that"
///
/// He reset the simulation account. NinjaTrader put the balance back and zeroed
/// its own P&L; Ballast was told nothing, so the row carried on reporting "was
/// up $2,726, handed back $2,726" about money the account no longer has any
/// record of.
///
/// The detection is the easy half. The hard half is that the accumulators being
/// cleared are the same ones holding the latched daily loss limit, and un-
/// spending a day on a live account would undo the most important thing Ballast
/// does. So nothing clears itself: a reset raises an offer and waits.
///
/// The discriminator is that trading cannot move the balance of a flat account.
/// Money that moves with no fill behind it did not come from the market.
/// </summary>
public static class ResetTests
{
    public static void Run()
    {
        ARealResetIsSpotted();
        WinningBackToExactlyZeroIsNotAReset();
        AQuietAccountIsNotAReset();
        CommissionPostingIsNotAReset();
        NothingIsClearedUntilTheTraderSaysSo();
        StartingOverReAnchorsTheFloor();
        AResizedAccountIsCaughtAfterARestart();
        OnlySimAccountsGetTheOneClickVersion();
        AMirroredReplayIsNotASecondTrade();
        AnEvalThatComesBackADifferentSizeIsNotTrusted();
        AnOrdinaryTradeIsNotAReset();
    }

    static readonly DateTime T0 = new DateTime(2026, 8, 5, 10, 0, 0);

    static BallastTracker Fresh()
    {
        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 5000;
        t.Config.DailyLossLimit = 1200;
        t.Config.DailyTarget = 1500;
        t.Config.MaxTrades = 12;
        t.Config.MaxLossesBeforeStop = 6;
        t.Config.CooldownMinutes = 0;
        t.Config.TrustAccountRealised = false;
        t.EnsureSession(T0, 0, 100000);
        t.OnEquity(100000, 0);
        return t;
    }

    /// <summary>One round-trip: open, close, and the equity tick that follows.</summary>
    static void Trade(BallastTracker t, DateTime at, double realisedBefore, double pnl)
    {
        t.OnPosition(1, realisedBefore, at, "NQ SEP26", "Sim103");
        t.OnPosition(0, realisedBefore + pnl, at.AddMinutes(2), "NQ SEP26", "Sim103");
        t.OnEquity(100000 + realisedBefore + pnl, realisedBefore + pnl);
    }

    /// <summary>
    /// "this keeps happening at least on the sim accounts so im not sure why it
    /// is doing that"
    ///
    /// It was asking whether an account had been reset after ordinary trades.
    /// The screenshot carried its own disproof: the row read "green $708" while
    /// the question underneath claimed the P&L was back to zero.
    ///
    /// The old test was "cash moved by more than $500 with no fill seen since
    /// the last equity tick". But the fill flag is cleared on EVERY equity tick,
    /// and NinjaTrader does not promise the cash update lands in the same beat
    /// as the execution - so close, stale tick, fresh tick reads as a balance
    /// that moved with nothing behind it. It hit the sims because that is where
    /// the size is: one MNQ on the funded accounts rarely clears $500, and NQ
    /// usually does.
    /// </summary>
    static void AnOrdinaryTradeIsNotAReset()
    {
        T.S("an ordinary trade is not a reset");

        // His Sim101, to the dollar: six trades, a $666 hole, and $708 up.
        BallastTracker t = Fresh();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.MaxTrades = 7;
        t.Config.MaxLossesBeforeStop = 3;

        double realised = 0;
        double[] day = new double[] { -666.04, 540, -300, 620, -180, 693.84 };

        for (int n = 0; n < day.Length; n++)
        {
            DateTime at = T0.AddMinutes(n * 12);
            t.OnPosition(1, realised, at, "NQ SEP26", "Sim101");
            realised += day[n];
            t.OnPosition(0, realised, at.AddMinutes(3), "NQ SEP26", "Sim101");

            // The shape that broke it: the equity tick after the close carries
            // the OLD cash, and the new figure only arrives on the tick after.
            t.OnEquity(100000 + realised - day[n], realised);
            t.OnEquity(100000 + realised, realised);
            t.OnEquity(100000 + realised, realised);

            T.Ok(!t.ResetSuspected,
                 "after trade " + (n + 1) + " (" + day[n] + ") nothing is suspected");
        }

        T.Eq(t.TradesToday, 6, "six trades");
        T.Near(t.DailyPnl, 707.8, 0.01, "and $707.80 up, exactly as his row said");

        // The thing it is FOR still works: put back to the start with no fill.
        t.OnEquity(100000, 0);
        t.OnEquity(100000, 0);
        T.Ok(t.ResetSuspected, "an account put back to its starting figure is still caught");
    }

    static void ARealResetIsSpotted()
    {
        T.S("an account put back to the start is noticed");

        BallastTracker t = Fresh();
        Trade(t, T0, 0, 2726);                       // his figure exactly
        T.Near(t.PeakDailyPnl, 2726, 0.01, "the day peaked at 2,726");
        T.Ok(!t.ResetSuspected, "and nothing looks wrong yet");

        // The reset: balance back to 100,000, platform P&L zeroed, no fill.
        t.OnEquity(100000, 0);
        T.Ok(!t.ResetSuspected, "one reading is not enough - a fill's position update may "
                              + "simply not have landed yet");

        t.OnEquity(100000, 0);
        T.Ok(t.ResetSuspected, "the balance moved with no trade behind it");
        T.Near(t.PeakDailyPnl, 2726, 0.01, "but the peak is still there - nothing was cleared");
        T.Eq(t.TradesToday, 1, "and neither were the counts");
    }

    static void WinningBackToExactlyZeroIsNotAReset()
    {
        T.S("winning back to exactly zero is not a reset");

        // The case this whole design exists to protect. Down 2,000 past a 1,200
        // limit, then a winner that lands the day on exactly 0.00. Realised is
        // zero and the account is flat - every condition but the one that
        // matters, which is that a fill got it there.
        BallastTracker t = Fresh();
        Trade(t, T0, 0, -2000);
        T.Ok(t.DailyLossLimitHit, "the limit was hit");

        Trade(t, T0.AddMinutes(20), -2000, 2000);
        T.Near(t.DailyPnl, 0, 0.01, "the day is back to exactly zero");
        T.Ok(!t.ResetSuspected, "and that is emphatically not a reset");
        T.Ok(t.DailyLossLimitHit, "so the day stays spent");
        T.Eq(t.TradesToday, 2, "and both trades still count");
    }

    static void AQuietAccountIsNotAReset()
    {
        T.S("an account that has done nothing is not reset, it is quiet");

        BallastTracker t = Fresh();
        t.OnEquity(100000, 0);
        t.OnEquity(100000, 0);
        T.Ok(!t.ResetSuspected, "no trades, no peak, nothing to erase");
    }

    static void CommissionPostingIsNotAReset()
    {
        T.S("a small unexplained adjustment is not a reset");

        BallastTracker t = Fresh();
        Trade(t, T0, 0, 500);

        // Something adjusts the balance by a few dollars with no fill. Real, and
        // not a reset - the threshold is there so this cannot masquerade as one.
        t.OnEquity(100460, 460);
        t.OnEquity(100460, 460);
        T.Ok(!t.ResetSuspected, "40 dollars is a fee, not a fresh account");
    }

    static void NothingIsClearedUntilTheTraderSaysSo()
    {
        T.S("nothing is cleared until the trader says so");

        BallastTracker t = Fresh();
        Trade(t, T0, 0, -1500);
        T.Ok(t.DailyLossLimitHit, "the day is spent");

        t.OnEquity(100000, 0);
        t.OnEquity(100000, 0);
        T.Ok(t.ResetSuspected, "a reset is suspected");
        T.Ok(t.DailyLossLimitHit, "and the limit is STILL latched while it is only suspected");

        DisciplineInput held = t.BuildInput(T0.AddMinutes(30));
        T.Eq(DisciplineEngine.Evaluate(held).Action, DisciplineAction.Lockout,
             "the account is still locked out on a merely suspected reset");

        t.StartOver(T0.AddMinutes(31), 0, 100000);

        T.Ok(!t.DailyLossLimitHit, "once confirmed, the day is genuinely fresh");
        T.Eq(t.TradesToday, 0, "the counts are cleared");
        T.Near(t.PeakDailyPnl, 0, 0.01, "and so is the peak");
        T.Near(t.WorstDailyPnl, 0, 0.01, "and the trough");
        T.Ok(!t.ResetSuspected, "and the offer is withdrawn");
        T.Eq(t.RestartedAt, T0.AddMinutes(31), "the restart is timestamped so a reload agrees");
    }

    /// <summary>
    /// His actual case, and the one the first version missed.
    ///
    /// He did not wipe the day's P&L - he changed what the simulation account
    /// HOLDS, from 100,000 to 150,000, and he did it between 11:36 and 12:18
    /// while Ballast was shut. So there was no jump for anything watching live to
    /// see, and the stale "was up 2,726" survived into the afternoon.
    ///
    /// Cash minus realised is what catches it. Trading moves both together, so
    /// that figure should not shift once all day - and unlike a jump, it can be
    /// written into the session file and compared against on the way back up.
    /// </summary>
    static void AResizedAccountIsCaughtAfterARestart()
    {
        T.S("an account re-sized while Ballast was closed is caught on reopening");

        BallastTracker before = Fresh();
        Trade(before, T0, 0, 2726);
        T.Near(before.DayOpenBalance, 100000, 0.01,
               "the day opened on 100,000 and trading did not move that");

        // Ballast closes. The account is re-sized to 150,000. Ballast reopens and
        // reads the saved figure back out of the session file.
        // The config is set to match the account's new size, as he did - so the
        // 152,726 reading is believable and it is only the BASE that disagrees.
        TrackerConfig bigger = new TrackerConfig();
        bigger.StartingBalance = 150000;
        bigger.TrailingDrawdown = 5000;
        bigger.TrustAccountRealised = false;

        BallastTracker after = new BallastTracker();
        after.Config = bigger;
        after.EnsureSession(T0.AddHours(1), 2726, 152726);
        after.SeedDayOpen(100000);
        after.SeedSession(T0.Date, 0, 152726, 2726, 0, false, 152726, 152726, 0, false);

        after.OnEquity(152726, 2726, 152726);
        after.OnEquity(152726, 2726, 152726);

        T.Ok(after.ResetSuspected,
             "cash minus realised is 150,000 where it was 100,000 - the account is not "
           + "the one those figures were about");
    }

    /// <summary>
    /// "yea apply it to the now page so i dont have so many clicks but just on
    /// sim accounts...dont do it to non sim accounts"
    ///
    /// The one-click version on the Now page un-spends a day. On a simulation
    /// account that is a convenience; beside a funded account it is one misplaced
    /// click away from erasing a day the trader actually lived through. So when
    /// the provider cannot be read and the name is all there is to go on, the
    /// test has to be exact - the stem must BE one of NinjaTrader's own account
    /// names, not merely contain it.
    /// </summary>
    static void OnlySimAccountsGetTheOneClickVersion()
    {
        T.S("only NinjaTrader's own sim accounts are recognised by name");

        T.Ok(RuleBook.IsBuiltInSimName("Sim101"), "Sim101 is one");
        T.Ok(RuleBook.IsBuiltInSimName("Sim103"), "and so is Sim103");
        T.Ok(RuleBook.IsBuiltInSimName("Sim"), "so is the bare name");
        T.Ok(RuleBook.IsBuiltInSimName("Playback101"), "and Playback101");
        T.Ok(RuleBook.IsBuiltInSimName("Backtest"), "and Backtest");
        T.Ok(RuleBook.IsBuiltInSimName(" sim104 "), "case and spacing do not matter");

        // His own funded accounts, and the near misses that a substring match
        // would have handed a day-erasing button to.
        T.Ok(!RuleBook.IsBuiltInSimName("APEX-11325-106"), "a funded account is not");
        T.Ok(!RuleBook.IsBuiltInSimName("PA-APEX-11325-04"), "nor a performance account");
        T.Ok(!RuleBook.IsBuiltInSimName("SimplyFunded"), "nor a firm whose name starts the same");
        T.Ok(!RuleBook.IsBuiltInSimName("Sim - live money"), "nor one a trader named badly");
        T.Ok(!RuleBook.IsBuiltInSimName("MySim101"), "nor one that merely ends that way");
        T.Ok(!RuleBook.IsBuiltInSimName("Sim1132511"), "nor a long account number after it");
        T.Ok(!RuleBook.IsBuiltInSimName(""), "and an empty name is nothing at all");
        T.Ok(!RuleBook.IsBuiltInSimName(null), "neither is no name");
    }

    /// <summary>
    /// "plus it is asking me again i believe about the same trade that i already
    /// entered on the second one but im not sure if that is the same trade or one
    /// from a while ago."
    ///
    /// It was the same trade. One winning long on APEX-11325-106 was written into
    /// the journal twice: once correctly as Long 09:36:01-09:37:50 +$880, and
    /// once mirrored as Short 09:37:50-09:36:01 +$880 - same money, same size,
    /// direction inverted, entry and exit swapped.
    ///
    /// NinjaTrader replays a position's executions when Ballast subscribes to an
    /// account, and not always in the order they happened. The closing SELL of a
    /// long, arriving first, is indistinguishable from the opening of a short.
    ///
    /// The duplicate row was the least of it. The day's watched total was then
    /// $880 too high, so the gap reconciler booked an $884 "trade while Ballast
    /// was closed" to make the arithmetic balance. One real winning trade became
    /// three trades and a loss, on an account whose rule stops him at three.
    /// </summary>
    static void AMirroredReplayIsNotASecondTrade()
    {
        T.S("a round trip cannot end before it began");

        BallastTracker t = Fresh();

        // The real trade: long at 09:36:01, out at 09:37:50, up 880.
        DateTime entry = new DateTime(2026, 8, 6, 9, 36, 1);
        DateTime exit = new DateTime(2026, 8, 6, 9, 37, 50);
        t.OnPosition(1, 0, entry, "NQ SEP26", "APEX-11325-106");
        BallastTrade real = t.OnPosition(0, 880, exit, "NQ SEP26", "APEX-11325-106");

        T.Ok(real != null, "the trade is recorded");
        T.Eq(t.TradesToday, 1, "and counted once");

        // The replay: the closing sell arrives looking like a new short at the
        // EXIT time, then the earlier buy closes it at the ENTRY time.
        t.OnPosition(-1, 880, exit, "NQ SEP26", "APEX-11325-106");
        BallastTrade ghost = t.OnPosition(0, 1760, entry, "NQ SEP26", "APEX-11325-106");

        T.Ok(ghost == null, "the mirror image is not a trade and never reaches the journal");
        T.Eq(t.TradesToday, 1, "the day still holds one trade, not two");
        T.Eq(t.LossStreak, 0, "and no loss is invented");

        // And the next real trade still works - the tracker is not left half open.
        DateTime later = new DateTime(2026, 8, 6, 10, 15, 0);
        t.OnPosition(1, 880, later, "NQ SEP26", "APEX-11325-106");
        BallastTrade next = t.OnPosition(0, 680, later.AddMinutes(3), "NQ SEP26", "APEX-11325-106");
        T.Ok(next != null, "the account is not left stuck in a position that never was");
        T.Eq(t.TradesToday, 2, "and the day counts on normally");
        T.Eq(t.LossStreak, 1, "including the loss that really happened");
    }

    /// <summary>
    /// "eval accounts do get reset if they ever go below the floor, so does the
    /// system understand that when it comes back next month with a different
    /// starting balance potentially"
    ///
    /// Two cases, and they are not the same.
    ///
    /// Reset to the SAME size is handled - see StartingOverReAnchorsTheFloor. The
    /// peak comes back down with the balance, so the floor returns to a full
    /// drawdown below and the account has all its room again.
    ///
    /// Reset to a DIFFERENT size is not something Ballast can work out for
    /// itself, because the settings are the only thing that says how big the
    /// account is. What it must never do is carry on quoting a cushion from the
    /// old size - a 250K account's floor on a 50K account reports tens of
    /// thousands of room that does not exist. So it stops quoting, says the two
    /// figures cannot both be true, and asks. Amber, not red: nothing has gone
    /// wrong with the trading.
    /// </summary>
    static void AnEvalThatComesBackADifferentSizeIsNotTrusted()
    {
        T.S("an eval that comes back a different size is questioned, not guessed at");

        // Set up as the 250K he ran last month; reset has delivered a 50K.
        DisciplineInput i = new DisciplineInput();
        i.StartingBalance = 250000;
        i.TrailingDrawdown = 6500;
        i.CurrentEquity = 50000;
        i.HasValidEquity = true;
        i.FloorLevel = 243500;
        i.CushionToFloor = 50000 - 243500;
        i.MaxTrades = 5;
        i.MaxLossesBeforeStop = 3;

        T.Ok(i.ConfigMismatch, "the size and the balance cannot both be true");
        T.Ok(!i.PastFloor, "and it is NOT called a dead account");

        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        T.Eq(d.Action, DisciplineAction.CheckSetup, "it asks about the account");
        T.Eq(d.Urgency, Urgency.Caution, "in amber - the trading is not the problem");

        string row = DisciplineEngine.RowWarning(i, d);
        T.Ok(row.IndexOf("250,000") >= 0 && row.IndexOf("50,000") >= 0,
             "stating both figures so the contradiction is visible: " + row);

        // The same eval reset back to the size it already was needs no help at
        // all, and must not be dragged into this.
        DisciplineInput same = new DisciplineInput();
        same.StartingBalance = 250000;
        same.TrailingDrawdown = 6500;
        same.CurrentEquity = 250000;
        same.HasValidEquity = true;
        same.FloorLevel = 243500;
        same.CushionToFloor = 6500;
        same.MaxTrades = 5;
        same.MaxLossesBeforeStop = 3;

        T.Ok(!same.ConfigMismatch, "a reset to the same size is just an account at its start");
        T.Near(same.CushionToFloor, 6500, 0.01, "with the whole drawdown in front of it");
    }

    static void StartingOverReAnchorsTheFloor()
    {
        T.S("starting over moves the floor back with the balance");

        BallastTracker t = Fresh();
        Trade(t, T0, 0, 4000);                       // peak equity 104,000
        T.Near(t.PeakEquity, 104000, 0.01, "the peak equity followed the profit up");

        DisciplineInput before = t.BuildInput(T0.AddMinutes(10));
        T.Near(before.FloorLevel, 99000, 0.01, "so the trailing floor sits at 99,000");

        t.OnEquity(100000, 0);
        t.OnEquity(100000, 0);
        t.StartOver(T0.AddMinutes(11), 0, 100000);

        // The expensive one to get wrong. Leave the peak at 104,000 and the floor
        // stays at 99,000 on an account that now holds 100,000 - a thousand of
        // cushion where there should be five.
        T.Near(t.PeakEquity, 100000, 0.01, "the peak comes back down with the account");

        DisciplineInput after = t.BuildInput(T0.AddMinutes(12));
        T.Near(after.FloorLevel, 95000, 0.01, "and the floor is a full drawdown below again");
        T.Near(after.CushionToFloor, 5000, 0.01, "with the whole drawdown to lose");
    }
}
