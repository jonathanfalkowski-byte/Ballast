// ─────────────────────────────────────────────────────────────────────────────
// Ballast — PlanSizeTests.cs
//
// The plan size, and the wall that depends on it.
//
// Why this exists: a journal of 167 hand-traded trades had 11 taken off-plan
// while the day was already green. Ten of the eleven were above the planned
// size and every one of them was INSIDE the configured cap of four, so the
// existing over-size signal could not see any of them. One win in eleven, and
// together they lost more than all four of the trader's setups made.
//
// So the cap and the plan are different numbers and Ballast now holds both.
// These tests pin the distinction down, because collapsing them back into one
// field is the obvious "simplification" that would silently delete the warning.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Ballast;

static class PlanSizeTests
{
    public static void Run()
    {
        SilentWhenUnsaid();
        FiresOnlyWhenGreenAndOverPlan();
        CapCannotSubstituteForPlan();
        SurvivesTheSettingsFile();
        OldFilesStillLoad();
        ReachesTheWall();
    }

    /// <summary>A green day, over the plan, with the plan set.</summary>
    static DisciplineInput Base()
    {
        DisciplineInput i = new DisciplineInput();
        i.CurrentEquity = 251000; i.FloorLevel = 244500; i.CushionToFloor = 6500;
        i.DailyLossLimit = 3000; i.MaxLossesBeforeStop = 3; i.MaxTrades = 5;
        i.MaxContracts = 4; i.NowMinuteEt = 600; i.MinutesSinceLastLoss = -1;
        i.DailyTarget = 1200;
        i.TradesToday = 3;
        i.HasValidEquity = true;
        i.DailyPnl = 900; i.PeakDailyPnl = 900;
        i.PlanContracts = 1; i.OpenContracts = 4;
        return i;
    }

    static bool HasGreenSize(DisciplineInput i)
    {
        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        for (int n = 0; n < d.Signals.Count; n++)
            if (d.Signals[n].Key == "green_size") return true;
        return false;
    }

    static void SilentWhenUnsaid()
    {
        T.S("a trader who never gave a plan size is not warned about it");

        DisciplineInput i = Base();
        i.PlanContracts = 0;              // never said
        T.Ok(!HasGreenSize(i), "no plan means no warning, rather than a guessed one");

        // And nothing else about the account changed, so the rest still works.
        i.PlanContracts = 1;
        T.Ok(HasGreenSize(i), "saying it switches the warning on");
    }

    static void FiresOnlyWhenGreenAndOverPlan()
    {
        T.S("the warning needs BOTH a green day and a size above the plan");

        DisciplineInput i = Base();
        T.Ok(HasGreenSize(i), "up on the day and four times the plan fires it");

        i.OpenContracts = 1;
        T.Ok(!HasGreenSize(i), "at the planned size it stays quiet, however green the day");

        i.OpenContracts = 4;
        i.DailyPnl = -900; i.PeakDailyPnl = 0;
        T.Ok(!HasGreenSize(i), "and oversized while DOWN is a different problem, not this one");

        // Down and oversized is what over_size and the loss walls are for. This
        // one is only ever about a day that is going well, which is the whole
        // reason it had to be written separately.
        i.DailyPnl = 1; i.PeakDailyPnl = 1;
        T.Ok(HasGreenSize(i), "a dollar up still counts as up");
    }

    static void CapCannotSubstituteForPlan()
    {
        T.S("the cap cannot stand in for the plan - this is the case that was missed");

        // Exactly the shape of the 11 trades: four contracts against a plan of
        // one, on an account whose cap is four. Nothing is over the cap.
        DisciplineInput i = Base();
        i.MaxContracts = 4; i.OpenContracts = 4; i.PlanContracts = 1;

        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        bool overSize = false, greenSize = false;
        for (int n = 0; n < d.Signals.Count; n++)
        {
            if (d.Signals[n].Key == "over_size")  overSize = true;
            if (d.Signals[n].Key == "green_size") greenSize = true;
        }
        T.Ok(!overSize, "the cap is not breached, so the old signal is correctly silent");
        T.Ok(greenSize, "but the plan is, and that is the trade that costs the money");
    }

    static void SurvivesTheSettingsFile()
    {
        T.S("the plan size is remembered between sessions");

        TrackerConfig c = new TrackerConfig();
        c.MaxContracts = 4;
        c.PlanContracts = 1;

        string key;
        TrackerConfig back = SettingsCodec.Deserialise(SettingsCodec.Serialise("Sim110", c), out key);
        T.Eq(key, "Sim110", "the account key round trips");
        T.Eq(back.PlanContracts, 1, "and so does the plan size");
        T.Eq(back.MaxContracts, 4, "without disturbing the cap it sits next to");
    }

    static void OldFilesStillLoad()
    {
        T.S("a settings file written before the plan size existed still loads");

        // Serialise, then chop the new trailing field off, exactly as an older
        // Ballast would have written it.
        TrackerConfig c = new TrackerConfig();
        c.MaxContracts = 4; c.PlanContracts = 3; c.DailyTarget = 1200;
        string line = SettingsCodec.Serialise("Sim110", c);
        int cut = line.LastIndexOf('|');
        string older = line.Substring(0, cut);

        string key;
        TrackerConfig back = SettingsCodec.Deserialise(older, out key);
        T.Ok(back != null, "the older line is still readable rather than dropped");
        T.Eq(back.PlanContracts, 0, "the missing field reads as unsaid");
        T.Eq(back.MaxContracts, 4, "and every field before it is unmoved");
        T.Near(back.DailyTarget, 1200, 0.01, "including the ones the trader had set");
    }

    static void ReachesTheWall()
    {
        T.S("and it actually reaches the wall, not just the row");

        DisciplineInput i = Base();
        DisciplineDecision d = DisciplineEngine.Evaluate(i);

        System.Collections.Generic.List<TiltTrigger> all =
            TiltLockout.EvaluateAll("APEX-11325-105", i, d, true);

        bool found = false;
        for (int n = 0; n < all.Count; n++)
            if (all[n].Kind == TiltKind.GreenSize)
            {
                found = true;
                T.Ok(all[n].Line.IndexOf("plan of 1") >= 0,
                     "the wall says what the plan was: " + all[n].Line);
                T.Ok(all[n].Ask.IndexOf("Which setup is this?") >= 0,
                     "and asks the question that does the work");
            }
        T.Ok(found, "the wall is raised");

        // Soft on purpose. It is fitted to one journal and will be wrong
        // sometimes; being wrong must cost a question, not a blocked screen.
        T.Ok(!TiltLockout.IsHardBreaker(TiltKind.GreenSize),
             "but it is not a hard breaker - it asks rather than forbids");

        // A bot sizing up on a green day is not a person getting carried away.
        i.IsAutomated = true;
        T.Eq(TiltLockout.EvaluateAll("APEX-11325-105", i, DisciplineEngine.Evaluate(i), true).Count, 0,
             "and a bot never gets it, like every other wall");
    }
}
