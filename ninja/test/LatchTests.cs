using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "i actually went over the amount of loss for the day but i went in one more
/// trade and it was a winner and brought the amount i had lost below the total
/// loss for the day so now it is in the yellow but it should stay red once you
/// hit the bad."
///
/// He is right, and this was the worst-shaped bug in the product. Hitting a
/// daily loss limit was read as a STATE - "am I down more than my limit right
/// now?" - rather than as an EVENT. So going past the limit, taking one more
/// trade and winning turned a hard stop back into a caution.
///
/// The rule un-fired. And it un-fired as a direct reward for taking the trade
/// the rule existed to prevent, teaching the exact lesson the whole product
/// argues against: that you can trade your way back out of a bad day.
/// </summary>
public static class LatchTests
{
    public static void Run()
    {
        WinningItBackDoesNotUndoIt();
        TheLatchIsPerAccount();
        ANewDayStartsClean();
        ClosingTheWindowDoesNotUnspendTheDay();
        TheWallDoesNotFollowYouAroundAllDay();
        StandingDownIsRememberedAndCountedOnce();
        StandingDownCanBeTakenBack();
    }

    /// <summary>
    /// "we should probably have a Done for the Day button on ballast"
    ///
    /// One button that ends the session on every watched account at once. A
    /// decision made by accident has to be reversible, or it becomes a button
    /// nobody dares press - and a stand-down button nobody presses is worse than
    /// no button, because the alternative is trading on.
    ///
    /// Taking it back is deliberately not free: it clears every hold on the
    /// account, so the next line crossed raises its wall again from nothing.
    /// Un-silenced, not forgotten.
    /// </summary>
    static void StandingDownCanBeTakenBack()
    {
        T.S("standing down can be taken back");

        DateTime t0 = new DateTime(2026, 8, 6, 11, 0, 0);
        TiltGate gate = new TiltGate();

        gate.ReleaseAccountForDay("APEX-11325-105", t0);
        gate.ReleaseAccountForDay("APEX-11325-106", t0);

        T.Ok(gate.IsReleased("APEX-11325-105", TiltKind.LossStreak, t0.AddHours(1)),
             "both accounts are quiet for the rest of the day");
        T.Ok(gate.IsReleased("APEX-11325-106", TiltKind.LossStreak, t0.AddHours(1)),
             "including the one that was not at a line");

        gate.ClearAccount("APEX-11325-105");

        T.Ok(!gate.IsReleased("APEX-11325-105", TiltKind.LossStreak, t0.AddHours(1)),
             "the one taken back can raise a wall again");
        T.Ok(gate.IsReleased("APEX-11325-106", TiltKind.LossStreak, t0.AddHours(1)),
             "and the other is untouched - it is per account, not a global undo");

        // Tomorrow is clean regardless, which was already true and must stay so.
        T.Ok(!gate.IsReleased("APEX-11325-106", TiltKind.LossStreak, t0.AddDays(1)),
             "and the release still expires overnight");
    }

    static BallastTracker Fresh(DateTime t0, double limit)
    {
        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.DailyLossLimit = limit;
        t.Config.MaxTrades = 20;
        t.Config.MaxLossesBeforeStop = 9;
        t.Config.CooldownMinutes = 0;
        // These cases drive Ballast's own baseline arithmetic directly, so they
        // measure from it rather than from the account's own realised figure.
        t.Config.TrustAccountRealised = false;
        t.EnsureSession(t0, 0, 100000);
        t.OnEquity(100000, 0);
        return t;
    }

    static void Trade(BallastTracker t, DateTime at, ref double realised, double pnl, string acct)
    {
        t.OnPosition(1, realised, at, "NQ SEP26", acct);
        realised += pnl;
        t.OnPosition(0, realised, at.AddMinutes(2), "NQ SEP26", acct);
        t.OnEquity(100000 + realised, realised);
    }

    static void WinningItBackDoesNotUndoIt()
    {
        T.S("winning it back does not give the day back");

        DateTime t0 = new DateTime(2026, 8, 3, 9, 40, 0);
        BallastTracker t = Fresh(t0, 2500);
        double realised = 0;

        // Down 1,200. Uncomfortable, not done.
        Trade(t, t0, ref realised, -1200, "Sim110");
        DisciplineDecision d = DisciplineEngine.Evaluate(t.BuildInput(t0.AddMinutes(5)));
        T.Ok(d.Action != DisciplineAction.Lockout, "down 1,200 of 2,500 is not a lockout");
        T.Ok(!t.DailyLossLimitHit, "and the limit has not been hit");

        // Down 2,700. Past the line.
        Trade(t, t0.AddMinutes(10), ref realised, -1500, "Sim110");
        DisciplineInput past = t.BuildInput(t0.AddMinutes(20));
        T.Near(past.DailyPnl, -2700, 0.01, "the day is 2,700 down");
        T.Ok(t.DailyLossLimitHit, "the limit has been hit");
        T.Eq(DisciplineEngine.Evaluate(past).Action, DisciplineAction.Lockout, "and that is a lockout");
        T.Eq(DisciplineEngine.Evaluate(past).Urgency, Urgency.Alert, "at full alarm");

        // THE BUG. One more trade, and it wins 1,400 back. The day is now only
        // 1,300 down - inside the limit again - and the account used to drop
        // back to a caution.
        Trade(t, t0.AddMinutes(30), ref realised, 1400, "Sim110");
        DisciplineInput back = t.BuildInput(t0.AddMinutes(40));

        T.Near(back.DailyPnl, -1300, 0.01, "the day is now only 1,300 down");
        T.Ok(back.DailyPnl > -back.DailyLossLimit, "which is inside the limit");
        T.Ok(back.DailyLossLimitHit, "but the limit was still hit today");
        T.Near(back.WorstDailyPnl, -2700, 0.01, "and the worst of it is remembered");

        DisciplineDecision after = DisciplineEngine.Evaluate(back);
        T.Eq(after.Action, DisciplineAction.Lockout,
             "so it stays a lockout - a winner does not un-hit the limit");
        T.Eq(after.Urgency, Urgency.Alert, "and stays red, not amber");

        // And it says why, rather than leaving a trader to wonder whether it is
        // stuck.
        string warn = DisciplineEngine.RowWarning(back, after);
        T.Ok(warn.IndexOf("does not give the day back") >= 0,
             "the row explains that winning some back changes nothing");

        bool explained = false;
        for (int n = 0; n < after.Signals.Count; n++)
        {
            if (after.Signals[n].Key != "daily_loss_limit") continue;
            explained = after.Signals[n].Summary.IndexOf("at the worst of it") >= 0;
        }
        T.Ok(explained, "and the reason quotes the worst the day actually got to");

        // Even a day that finishes green stays spent. This is the case that
        // matters most, because it is the one that feels most like permission.
        Trade(t, t0.AddMinutes(50), ref realised, 2000, "Sim110");
        DisciplineInput green = t.BuildInput(t0.AddHours(2));
        T.Near(green.DailyPnl, 700, 0.01, "the day has finished 700 up");
        T.Eq(DisciplineEngine.Evaluate(green).Action, DisciplineAction.Lockout,
             "and it is still done - the money came back, the day did not");
    }

    static void TheLatchIsPerAccount()
    {
        T.S("one account hitting its limit does not stop another");

        DateTime t0 = new DateTime(2026, 8, 3, 9, 40, 0);

        BallastTracker a = Fresh(t0, 500);
        BallastTracker b = Fresh(t0, 3000);

        double ra = 0, rb = 0;
        Trade(a, t0, ref ra, -900, "APEX-105");
        Trade(b, t0, ref rb, -900, "APEX-106");

        T.Ok(a.DailyLossLimitHit, "the account with the 500 limit has spent its day");
        T.Ok(!b.DailyLossLimitHit, "the account with the 3,000 limit has not");

        T.Eq(DisciplineEngine.Evaluate(a.BuildInput(t0.AddHours(1))).Action,
             DisciplineAction.Lockout, "so one is locked out");
        T.Ok(DisciplineEngine.Evaluate(b.BuildInput(t0.AddHours(1))).Action != DisciplineAction.Lockout,
             "and the other is not - same loss, different limits, different answers");
    }

    static void ANewDayStartsClean()
    {
        T.S("tomorrow is a new day");

        DateTime t0 = new DateTime(2026, 8, 3, 9, 40, 0);
        BallastTracker t = Fresh(t0, 500);

        double realised = 0;
        Trade(t, t0, ref realised, -900, "Sim110");
        T.Ok(t.DailyLossLimitHit, "today is spent");

        // Next session.
        t.EnsureSession(t0.AddDays(1), realised, 100000 + realised);
        t.OnEquity(100000 + realised, realised);

        T.Ok(!t.DailyLossLimitHit, "a new session starts with the day unspent");
        T.Near(t.WorstDailyPnl, 0, 0.01, "and no trough carried over");
        T.Ok(DisciplineEngine.Evaluate(t.BuildInput(t0.AddDays(1).AddHours(1))).Action
             != DisciplineAction.Lockout, "so tomorrow is clear to trade");
    }

    static void ClosingTheWindowDoesNotUnspendTheDay()
    {
        T.S("closing Ballast does not un-spend the day");

        DateTime day = new DateTime(2026, 8, 3);
        DateTime t0 = day.AddHours(9).AddMinutes(40);

        // The morning: down 2,700 at the worst, back to 1,300 down by lunchtime.
        // The window is then closed and reopened, so the tracker is brand new and
        // is told what the journal says.
        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.DailyLossLimit = 2500;
        t.Config.MaxTrades = 20;
        t.Config.MaxLossesBeforeStop = 9;
        t.Config.TrustAccountRealised = false;

        t.SeedToday(day, 3, 2, t0.AddMinutes(12), false, -1300, -2700);
        t.EnsureSession(t0.AddHours(3), 0, 98700);
        t.OnEquity(98700, 0);

        T.Near(t.DailyPnl, -1300, 0.01, "the day's P&L comes back");
        T.Near(t.WorstDailyPnl, -2700, 0.01, "and so does the worst it got to");
        T.Ok(t.DailyLossLimitHit, "so the limit is still hit");
        T.Eq(DisciplineEngine.Evaluate(t.BuildInput(t0.AddHours(4))).Action,
             DisciplineAction.Lockout,
             "and closing the window is not a way to get the afternoon back");

        // A morning that never reached the limit must not be latched by the seed.
        BallastTracker ok = new BallastTracker();
        ok.Config = new TrackerConfig();
        ok.Config.StartingBalance = 100000;
        ok.Config.TrailingDrawdown = 6500;
        ok.Config.DailyLossLimit = 2500;
        ok.Config.MaxTrades = 20;
        ok.Config.MaxLossesBeforeStop = 9;
        ok.Config.TrustAccountRealised = false;

        ok.SeedToday(day, 2, 1, t0.AddMinutes(12), false, -300, -900);
        ok.EnsureSession(t0.AddHours(3), 0, 99700);
        ok.OnEquity(99700, 0);

        T.Ok(!ok.DailyLossLimitHit, "a morning that stayed inside the limit is not latched");
        T.Ok(DisciplineEngine.Evaluate(ok.BuildInput(t0.AddHours(4))).Action
             != DisciplineAction.Lockout, "and that account is still trading");

        // The old five-argument seed must keep working, and must not invent a
        // trough deeper than the day itself.
        BallastTracker legacy = new BallastTracker();
        legacy.Config = new TrackerConfig();
        legacy.Config.DailyLossLimit = 2500;
        legacy.Config.TrustAccountRealised = false;
        legacy.SeedToday(day, 1, 1, t0, true, -400);
        legacy.EnsureSession(t0.AddHours(3), 0, 99600);
        legacy.OnEquity(99600, 0);
        T.Ok(!legacy.DailyLossLimitHit, "a seed with no trough uses the day's P&L and nothing worse");
    }

    static void TheWallDoesNotFollowYouAroundAllDay()
    {
        T.S("the wall is for the acute moment, the red is for the day");

        DisciplineInput down = new DisciplineInput();
        down.DailyLossLimit = 2500;
        down.DailyPnl = -2700;
        down.WorstDailyPnl = -2700;
        down.DailyLossLimitHit = true;
        down.HasValidEquity = true;
        down.CushionToFloor = 4000;
        down.MaxContracts = 2;

        // An account cannot be $2,700 down without having traded, and a wall now
        // refuses to appear on one that has not - after a daily-loss wall turned
        // up on a morning he had not opened the platform.
        down.TradesToday = 4;

        DisciplineDecision dd = DisciplineEngine.Evaluate(down);
        List<TiltTrigger> whileDown = TiltLockout.EvaluateAll("Sim110", down, dd, false);
        T.Ok(HasKind(whileDown, TiltKind.DailyLossLimit),
             "while actually down past the limit, the wall goes up");

        // Won some back. Still done for the day, still red - but the wall does
        // not keep reappearing every fifteen minutes for the rest of the session.
        DisciplineInput recovered = new DisciplineInput();
        recovered.DailyLossLimit = 2500;
        recovered.DailyPnl = -1300;
        recovered.WorstDailyPnl = -2700;
        recovered.DailyLossLimitHit = true;
        recovered.HasValidEquity = true;
        recovered.CushionToFloor = 4000;
        recovered.MaxContracts = 2;
        recovered.TradesToday = 5;

        DisciplineDecision rd = DisciplineEngine.Evaluate(recovered);
        T.Eq(rd.Action, DisciplineAction.Lockout, "the advice is still a lockout");

        List<TiltTrigger> afterwards = TiltLockout.EvaluateAll("Sim110", recovered, rd, false);
        T.Ok(!HasKind(afterwards, TiltKind.DailyLossLimit),
             "but the wall does not go back up - a wall that fires all day stops being a wall");

        // Go deeper again and it does come back, because that is a fresh breach.
        DisciplineInput deeper = new DisciplineInput();
        deeper.DailyLossLimit = 2500;
        deeper.DailyPnl = -3400;
        deeper.WorstDailyPnl = -3400;
        deeper.DailyLossLimitHit = true;
        deeper.HasValidEquity = true;
        deeper.CushionToFloor = 3000;
        deeper.MaxContracts = 2;
        deeper.TradesToday = 6;

        List<TiltTrigger> again = TiltLockout.EvaluateAll("Sim110", deeper,
                                                          DisciplineEngine.Evaluate(deeper), false);
        T.Ok(HasKind(again, TiltKind.DailyLossLimit),
             "losing further past the limit puts it straight back up");
    }

    /// <summary>
    /// "it asks me this question every time i open up the ballast and i said done
    /// several times and it is recording it but i have not done it 14 times in 30
    /// days we have only had this up and running for 2 days."
    ///
    /// Two faults, and the second one caused the first. The gate that records
    /// "leave this account alone until tomorrow" lived only in memory, so every
    /// restart put the same wall back in front of a decision already made - and
    /// every press of the button logged another event, so two days of use read as
    /// fourteen stand-downs.
    /// </summary>
    static void StandingDownIsRememberedAndCountedOnce()
    {
        T.S("standing down is remembered, and counted once");

        DateTime day = new DateTime(2026, 8, 3);
        DateTime at = day.AddHours(10);

        // ── The promise has to survive a restart ─────────────────────────────
        TiltGate gate = new TiltGate();
        gate.ReleaseAccountForDay("Sim110", at);
        T.Ok(gate.IsReleased("Sim110", TiltKind.LossStreak, at.AddMinutes(1)),
             "standing down covers the account");
        T.Ok(gate.IsReleased("Sim110", TiltKind.DailyLossLimit, at.AddHours(5)),
             "for every reason, all day");

        // Ballast is closed and reopened.
        TiltGate reopened = new TiltGate();
        reopened.Restore(gate.Serialise(), at.AddHours(1));
        T.Ok(reopened.IsReleased("Sim110", TiltKind.LossStreak, at.AddHours(1)),
             "and it is still standing after a restart - the wall does not ask again");
        T.Ok(!reopened.IsReleased("APEX-105", TiltKind.LossStreak, at.AddHours(1)),
             "while another account is untouched");

        // Tomorrow it is gone, because that is what "until tomorrow" means.
        TiltGate tomorrow = new TiltGate();
        tomorrow.Restore(gate.Serialise(), day.AddDays(1).AddHours(9));
        T.Ok(!tomorrow.IsReleased("Sim110", TiltKind.LossStreak, day.AddDays(1).AddHours(9)),
             "and a new day starts with the wall armed again");

        // A typed override buys minutes, and only minutes, across a restart too.
        TiltGate typed = new TiltGate();
        typed.Release("Sim110", TiltKind.DailyLossLimit, at);
        TiltGate typedBack = new TiltGate();
        typedBack.Restore(typed.Serialise(), at.AddMinutes(5));
        T.Ok(typedBack.IsReleased("Sim110", TiltKind.DailyLossLimit, at.AddMinutes(5)),
             "five minutes after an override it is still quiet");
        T.Ok(!typedBack.IsReleased("Sim110", TiltKind.DailyLossLimit, at.AddMinutes(20)),
             "and twenty minutes later it is not - restarting does not extend it");

        // Rubbish in the file is dropped, not loaded.
        TiltGate junk = new TiltGate();
        junk.Restore(new List<string> { "", "nonsense", "a|b", "Sim110|*|not a date" }, at);
        T.Ok(!junk.IsReleased("Sim110", "*", at), "an unreadable line releases nothing");

        // ── And it is counted once per day per account ───────────────────────
        TiltLog log = new TiltLog();
        for (int n = 0; n < 7; n++) log.Add(Stood("Sim110", day.AddHours(10 + n)));

        T.Eq(log.StoodCount(day.AddHours(18), 30), 1,
             "seven presses on one day on one account is one stand-down");

        log.Add(Stood("Sim110", day.AddDays(1).AddHours(10)));
        log.Add(Stood("Sim110", day.AddDays(1).AddHours(11)));
        T.Eq(log.StoodCount(day.AddDays(1).AddHours(18), 30), 2, "two days is two");

        log.Add(Stood("APEX-105", day.AddDays(1).AddHours(12)));
        T.Eq(log.StoodCount(day.AddDays(1).AddHours(18), 30), 3,
             "and a different account on the same day is its own");

        T.Ok(log.StoodSummary(day.AddDays(1).AddHours(18), 30).IndexOf("3 times") >= 0,
             "which is what the wall says back");

        // Overrides group the same way, and the money is summed per session
        // rather than counted per wall.
        TiltLog over = new TiltLog();
        TiltEvent a = Over("Sim110", day.AddHours(10), -500);
        TiltEvent b = Over("Sim110", day.AddHours(11), -300);
        over.Add(a); over.Add(b);
        a.PnlAfter = a.PnlAtEvent - 500;     // the day went on to lose more
        b.PnlAfter = b.PnlAtEvent - 300;

        T.Eq(over.OverrideCount(day.AddHours(18), 30), 1,
             "two overrides on one day on one account is one session");
        string sum = over.Summary(day.AddHours(18), 30);
        T.Ok(sum.IndexOf("once in the last 30 days") >= 0,
             "and the sentence says once, not twice");
        T.Ok(sum.IndexOf("$800") >= 0,
             "while the money is the whole session - both overrides added up");
    }

    static TiltEvent Stood(string account, DateTime at)
    {
        TiltEvent e = new TiltEvent();
        e.At = at;
        e.AccountName = account;
        e.Kind = TiltKind.LossStreak;
        e.Stood = true;
        return e;
    }

    static TiltEvent Over(string account, DateTime at, double pnl)
    {
        TiltEvent e = new TiltEvent();
        e.At = at;
        e.AccountName = account;
        e.Kind = TiltKind.DailyLossLimit;
        e.Stood = false;
        e.PnlAtEvent = pnl;
        e.PnlAfter = pnl;
        return e;
    }

    static bool HasKind(List<TiltTrigger> list, string kind)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Kind == kind) return true;
        return false;
    }
}
