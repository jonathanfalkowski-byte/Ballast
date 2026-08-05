using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "the message here doesnt quite make sense it says its past floor but it
/// isnt."
///
/// Sim103 was up $2,079.60 on the day, sitting at $102,079.60, and Ballast was
/// telling him STOP NOW - past floor, in red, on the window and across the
/// chart. The arithmetic was right: the account had been set up as an Apex 150K
/// with a $5,000 drawdown, so Ballast put its floor at $145,000 and correctly
/// observed that $102,079 is below it. The premise was wrong. A 100K sim account
/// was wearing a 150K account's numbers.
///
/// The bug is not the misconfiguration - that is the trader's to fix. The bug is
/// that Ballast could not tell a dead account from an impossible one, and shouted
/// its loudest warning about a thing that plainly had not happened. A stop that
/// fires when it obviously should not is worse than no stop, because it is the
/// reason the next one gets waved away.
///
/// The discriminator is that a funded account cannot be FAR below its floor. The
/// firm closes it the moment it touches. So a little past is a dead account; tens
/// of thousands past is a live account with the wrong settings on it.
/// </summary>
public static class MismatchTests
{
    public static void Run()
    {
        Sim103IsNotPastItsFloor();
        AGenuineBlowThroughIsStillReported();
        ProfitIsNeverAMismatch();
        AnUnconfiguredAccountIsNeverAMismatch();
        TheHeadlineCardIgnoresAMismatchedAccount();
        TheWallDoesNotGoUpForASettingsProblem();
    }

    static DisciplineInput In(double start, double drawdown, double equity)
    {
        DisciplineInput i = new DisciplineInput();
        i.StartingBalance = start;
        i.TrailingDrawdown = drawdown;
        i.CurrentEquity = equity;
        i.HasValidEquity = true;
        i.FloorLevel = start - drawdown;
        i.CushionToFloor = equity - i.FloorLevel;
        i.MaxTrades = 12;
        i.MaxLossesBeforeStop = 6;
        return i;
    }

    static void Sim103IsNotPastItsFloor()
    {
        T.S("an account up on the day is not told it is past its floor");

        // His figures exactly: Sim103, set up as an Apex 150K, holding 102,079.60
        // after a 2,079.60 winning day.
        DisciplineInput i = In(150000, 5000, 102079.60);
        i.DailyPnl = 2079.60;
        i.PeakDailyPnl = 2079.60;
        i.TradesToday = 1;

        T.Ok(i.ConfigMismatch, "the settings and the balance cannot both be true");
        T.Ok(!i.PastFloor, "so it is NOT reported as past its floor");

        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        T.Eq(d.Action, DisciplineAction.CheckSetup, "the account is asked about, not stopped");
        T.Eq(d.Urgency, Urgency.Caution, "amber, not red - nothing has gone wrong with the trading");

        bool floorSignal = false, mismatchSignal = false;
        for (int n = 0; n < d.Signals.Count; n++)
        {
            if (d.Signals[n].Key == "past_floor") floorSignal = true;
            if (d.Signals[n].Key == "config_mismatch") mismatchSignal = true;
        }
        T.Ok(!floorSignal, "no past-floor signal is raised");
        T.Ok(mismatchSignal, "a settings signal is raised instead");

        string row = DisciplineEngine.RowWarning(i, d);
        T.Ok(row.IndexOf("floor") < 0, "the row does not mention a floor it cannot know");
        T.Ok(row.IndexOf("150,000") >= 0 && row.IndexOf("102,080") >= 0,
             "it states both numbers so the contradiction is visible: " + row);
    }

    static void AGenuineBlowThroughIsStillReported()
    {
        T.S("a real blow-through is still called what it is");

        // A 50K with a 2,000 drawdown that gapped 3,000 through its floor. The
        // shortfall clears twice the drawdown, but not a fifth of the account,
        // so it is a dead account and not a typo.
        DisciplineInput blown = In(50000, 2000, 45000);
        T.Ok(!blown.ConfigMismatch, "5,000 down on a 50K is a plausible loss");
        T.Ok(blown.PastFloor, "and it is past the floor");
        T.Eq(DisciplineEngine.Evaluate(blown).Action, DisciplineAction.Lockout,
             "so it is a lockout, exactly as before");

        // Barely past, which is the ordinary shape of a finished account.
        DisciplineInput edge = In(250000, 6500, 243400);
        T.Ok(!edge.ConfigMismatch, "600 past a 250K floor is not a settings problem");
        T.Ok(edge.PastFloor, "it is a finished account");

        // Far enough past that no firm would have allowed it.
        DisciplineInput impossible = In(250000, 6500, 195000);
        T.Ok(impossible.ConfigMismatch,
             "55,000 below a 250K account the firm was policing cannot have happened");
    }

    static void ProfitIsNeverAMismatch()
    {
        T.S("being above the configured size is not a mismatch");

        DisciplineInput up = In(150000, 5000, 160000);
        T.Ok(!up.ConfigMismatch, "an account in profit is not questioned");
        T.Ok(!up.PastFloor, "and is nowhere near its floor");

        // Wrong in the other direction - a 250K set up as a 50K - leaves the
        // floor tighter than reality, which reports less room than there is.
        // That is the safe way to be wrong and is deliberately left alone.
        DisciplineInput small = In(50000, 2000, 250000);
        T.Ok(!small.ConfigMismatch, "an under-stated size is not flagged, only under-reported");
    }

    static void AnUnconfiguredAccountIsNeverAMismatch()
    {
        T.S("an account with nothing set up is not accused of anything");

        DisciplineInput blank = In(0, 0, 102079.60);
        T.Ok(!blank.ConfigMismatch, "no size and no drawdown means nothing to contradict");

        DisciplineInput noEquity = In(150000, 5000, 0);
        noEquity.HasValidEquity = false;
        T.Ok(!noEquity.ConfigMismatch, "and neither does an account with no balance yet");
        T.Ok(!noEquity.PastFloor, "which is also not past any floor");
    }

    static void TheHeadlineCardIgnoresAMismatchedAccount()
    {
        T.S("the closest-to-its-floor card skips an account it cannot measure");

        BallastMonitor m = new BallastMonitor();

        AccountSnapshot bad = new AccountSnapshot();
        bad.AccountName = "Sim103";
        bad.Input = In(150000, 5000, 102079.60);       // cushion reads -42,920

        AccountSnapshot real = new AccountSnapshot();
        real.AccountName = "APEX-11325-106";
        real.Input = In(250000, 6500, 247912);          // cushion reads 4,412

        List<AccountSnapshot> snaps = new List<AccountSnapshot>();
        snaps.Add(bad);
        snaps.Add(real);

        T.Near(m.MinCushion(snaps), 4412, 0.01,
               "the mismatched account's imaginary cushion does not take over the card");
    }

    static void TheWallDoesNotGoUpForASettingsProblem()
    {
        T.S("no tilt wall for a settings problem");

        DisciplineInput i = In(150000, 5000, 102079.60);
        i.DailyPnl = 2079.60;
        DisciplineDecision d = DisciplineEngine.Evaluate(i);

        List<TiltTrigger> walls = TiltLockout.EvaluateAll("Sim103", i, d, true);

        T.Eq(walls.Count, 0, "a wall is for a decision the trader is about to make badly, "
                           + "not for a number Ballast cannot work out");
    }
}
