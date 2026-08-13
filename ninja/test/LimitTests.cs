using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// Cover for: "i want different daily targets and daily losses for each
/// account... it wont let me do that and a different amount of trades for each
/// account.... and different losses in a row."
///
/// Three separate faults produced that, and each is pinned here.
///
///   1. Picking an account type wrote the FIRM's daily loss limit over the
///      trader's own. Apex publishes none, so choosing "Apex 250K" replaced
///      "stop me at $500" with 0 - no daily stop at all, and no warning.
///   2. "Copy to all" copied the DEFAULT config, not the one being edited, over
///      every account - including the account just typed into.
///   3. Nothing on the Setup page ever showed the four numbers the trader
///      chooses, so two accounts with completely different limits printed
///      identical lines. The window's rendering cannot be driven from here, but
///      the round trip that has to hold underneath it can.
/// </summary>
public static class LimitTests
{
    public static void Run()
    {
        FourLimitsDivergePerAccount();
        RuleBookKeepsTheTradersOwnDailyStop();
        FirmDailyLimitStillBinds();
        LimitsSurviveTheSettingsFile();
        EngineActsOnEachAccountsOwnNumbers();
        TheTradeCountStopsHimTheSameWayALossStreakDoes();
        ALossStreakIsActuallyAStreak();
        TheRowAndTheChartNeverDisagree();
        TypingPastTheWallGivesTheButtonsBack();
        ProtectItWaitsUntilThereIsSomethingToProtect();
    }

    /// <summary>
    /// "didnt even see the warning to stop. Bizaare we may need to fix that"
    ///
    /// Six trades on APEX-11325-105 against a limit of five. Everything Ballast
    /// was built to do had happened - the row was amber, the action read DONE
    /// TODAY, the chart carried "6 TRADES - AT YOUR LIMIT" - and he walked
    /// straight through all of it, because none of it was any different from
    /// what the chart says all day.
    ///
    /// Two holes. The trade count was the one line a trader draws that had no
    /// wall behind it: max losses got one, max trades got a colour. And the
    /// words did not change when he crossed - "at your limit" was said at five
    /// and again at six, so the moment of crossing looked exactly like the
    /// moment before it.
    /// </summary>
    static void TheTradeCountStopsHimTheSameWayALossStreakDoes()
    {
        T.S("the trade count stops him the way a loss streak does");

        // His account, his numbers, at the moment he reached the limit.
        DisciplineInput at = new DisciplineInput();
        at.StartingBalance = 250000; at.TrailingDrawdown = 6500;
        at.CurrentEquity = 247427; at.HasValidEquity = true;
        at.FloorLevel = 243500; at.CushionToFloor = 3927;
        at.MaxTrades = 5; at.TradesToday = 5;
        at.MaxLossesBeforeStop = 3; at.LossStreak = 1;
        at.DailyPnl = 46; at.PeakDailyPnl = 46; at.DailyTarget = 750;
        at.MaxContracts = 4; at.NowMinuteEt = 600; at.MinutesSinceLastLoss = -1;

        DisciplineDecision dAt = DisciplineEngine.Evaluate(at);
        T.Eq(dAt.Action, DisciplineAction.StopForDay, "at five, the account is done");

        // Red, not amber, and this changed once the chart was in the picture.
        // At the limit the wall goes up and the chart says STOP - so a quiet
        // amber row beside it was the two halves disagreeing about the same
        // account. The day being OVER is not a caution; the at/past distinction
        // belongs in the words, where it now lives.
        T.Eq(dAt.Urgency, Urgency.Alert, "red, because the day is over and the chart says so");
        T.Ok(DisciplineEngine.RowWarning(at, dAt).IndexOf("that is your limit") >= 0,
             "and the row says the day is done");

        List<TiltTrigger> wallAt = TiltLockout.EvaluateAll("APEX-11325-105", at, dAt, true);
        bool found = false;
        for (int n = 0; n < wallAt.Count; n++)
            if (wallAt[n].Kind == TiltKind.MaxTrades) found = true;
        T.Ok(found, "and there is now something standing in front of the sixth trade");

        // One trade later - the screenshot.
        DisciplineInput past = new DisciplineInput();
        past.StartingBalance = 250000; past.TrailingDrawdown = 6500;
        past.CurrentEquity = 247427; past.HasValidEquity = true;
        past.FloorLevel = 243500; past.CushionToFloor = 3927;
        past.MaxTrades = 5; past.TradesToday = 6;
        past.MaxLossesBeforeStop = 3; past.LossStreak = 1;
        past.DailyPnl = 46; past.PeakDailyPnl = 46; past.DailyTarget = 750;
        past.MaxContracts = 4; past.NowMinuteEt = 600; past.MinutesSinceLastLoss = -1;

        DisciplineDecision dPast = DisciplineEngine.Evaluate(past);
        T.Eq(dPast.Urgency, Urgency.Alert,
             "past the line it goes red, so crossing it LOOKS like crossing it");

        string row = DisciplineEngine.RowWarning(past, dPast);
        T.Ok(row.IndexOf("PAST your limit of 5") >= 0,
             "and the words move with him: " + row);
        T.Ok(row.IndexOf("at your limit") < 0,
             "rather than saying the same thing either side of the line");

        // The chart is told, which is what was missing - a count breach used to
        // light nothing, so the banner read the same as it does all morning.
        T.Ok(TiltLockout.IsHardBreaker(TiltKind.MaxTrades),
             "a line he drew himself counts as a breaker, the same as his loss limit");

        // A bot has no line to break, and an account with no limit set has no
        // line at all - neither gets a wall.
        DisciplineInput bot = past;
        bot.IsAutomated = true;
        T.Eq(TiltLockout.EvaluateAll("APEX-11325-105", bot, DisciplineEngine.Evaluate(bot), true).Count, 0,
             "a strategy is not talked out of its sixth trade");
        bot.IsAutomated = false;

        DisciplineInput none = new DisciplineInput();
        none.StartingBalance = 250000; none.TrailingDrawdown = 6500;
        none.CurrentEquity = 247427; none.HasValidEquity = true;
        none.FloorLevel = 243500; none.CushionToFloor = 3927;
        none.MaxTrades = 0; none.TradesToday = 40;
        none.MaxLossesBeforeStop = 3; none.NowMinuteEt = 600; none.MinutesSinceLastLoss = -1;

        List<TiltTrigger> noWall = TiltLockout.EvaluateAll("APEX-11325-105", none,
                                                          DisciplineEngine.Evaluate(none), true);
        for (int n = 0; n < noWall.Count; n++)
            T.Ok(noWall[n].Kind != TiltKind.MaxTrades,
                 "a trader who set no trade limit is never told he passed one");
    }

    /// <summary>
    /// "any hard breaker but if i type that sentence in it releases the buttons"
    ///
    /// Two flags that look the same and are not. Locked keeps the chart shouting
    /// after an override - that was never his to switch off, because the chart is
    /// where he is actually looking. OrderEntryBlocked follows the override
    /// exactly, because he asked for the buttons to be a speed bump he can choose
    /// to walk over rather than a lock somebody else holds the key to.
    ///
    /// The dangerous mistake would be the other way round: buttons that stay dead
    /// after he has said, in words, that he means to carry on. That is the state
    /// where a man with a position on cannot get to his own controls.
    /// </summary>
    static void TypingPastTheWallGivesTheButtonsBack()
    {
        T.S("typing past the wall gives the buttons back, the chart keeps shouting");

        DateTime now = new DateTime(2026, 8, 7, 10, 30, 0);
        string acct = "APEX-11325-105";

        BallastState.Clear(acct);

        // A hard breaker, not yet argued with.
        BallastState.PublishLock(acct, true, "You are done for the day.", now, true);
        AccountState s = BallastState.Get(acct, now);
        T.Ok(s != null, "the state is published");
        T.Ok(s.Locked, "the account is locked");
        T.Ok(s.OrderEntryBlocked, "and the entry buttons should be dead");
        T.Ok(BallastState.ChartBanner(s).IndexOf("STOP") >= 0, "the chart says stop");

        // He types the sentence. The wall comes down; the chart does not.
        BallastState.PublishLock(acct, true, "You are done for the day.", now, false);
        s = BallastState.Get(acct, now);
        T.Ok(s.Locked, "the breaker is still in force");
        T.Ok(!s.OrderEntryBlocked, "but the buttons come back, because he said so in words");
        T.Ok(BallastState.ChartBanner(s).IndexOf("STOP") >= 0,
             "and the chart still says stop - overriding buys quiet, not a clean chart");

        // The day ends, or the account recovers. Nothing is left switched off.
        BallastState.PublishLock(acct, false, "", now, false);
        s = BallastState.Get(acct, now);
        T.Ok(!s.Locked && !s.OrderEntryBlocked, "no breaker, nothing blocked");

        // And the flag can never be set without a breaker behind it - a bug that
        // said "block" on a clear account would kill a working chart's buttons
        // for no stated reason.
        BallastState.PublishLock(acct, false, "", now, true);
        s = BallastState.Get(acct, now);
        T.Ok(!s.OrderEntryBlocked,
             "asking to block a clear account blocks nothing");

        BallastState.Clear(acct);
    }

    /// <summary>
    /// "it says losses in a row but it really means losses in a day....you may
    /// want to correct the title of that column or change how the system works"
    ///
    /// He was right, and it went deeper than the column heading. The counter was
    /// a plain daily total that never came back down - while the risk signal was
    /// named loss_streak, the wall was TiltKind.LossStreak, and the column, the
    /// Setup page and the chart all said "losses in a row". Every label in the
    /// product described a streak and nothing underneath had ever been one.
    ///
    /// So a green day of loss, win, win, loss, win, loss ended with Ballast
    /// stopping him at "3 losses in a row - you said 3 was your line", about a
    /// day with no streak anywhere in it and money made.
    ///
    /// Losses scattered through a day are what the DOLLAR daily loss limit is
    /// for, and that is a line he sets separately on every account.
    /// </summary>
    static void ALossStreakIsActuallyAStreak()
    {
        T.S("a loss streak is actually a streak");

        DateTime t0 = new DateTime(2026, 8, 10, 10, 0, 0);

        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 250000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.DailyLossLimit = 2000;
        t.Config.MaxTrades = 20;
        t.Config.MaxLossesBeforeStop = 3;
        t.Config.TrustAccountRealised = false;
        t.EnsureSession(t0, 0, 250000);
        t.OnEquity(250000, 0);

        // His green day: loss, win, win, loss, win, loss. Three losers, no run.
        double realised = 0;
        double[] day = new double[] { -200, 400, 300, -150, 500, -100 };
        int[] expected = new int[] { 1, 0, 0, 1, 0, 1 };

        for (int n = 0; n < day.Length; n++)
        {
            DateTime at = t0.AddMinutes(n * 10);
            t.OnPosition(1, realised, at, "NQ SEP26", "APEX-11325-105");
            realised += day[n];
            t.OnPosition(0, realised, at.AddMinutes(2), "NQ SEP26", "APEX-11325-105");
            t.OnEquity(250000 + realised, realised);

            T.Eq(t.LossStreak, expected[n],
                 "after trade " + (n + 1) + " the run is " + expected[n]);
        }

        T.Eq(t.TradesToday, 6, "six trades were taken");
        T.Near(t.DailyPnl, 750, 0.01, "and the day made money");

        DisciplineInput up = t.BuildInput(t0.AddHours(1));
        T.Eq(up.LossStreak, 1, "one loss in a row, not the three he used to be stopped on");

        bool stopped = false;
        DisciplineDecision d = DisciplineEngine.Evaluate(up);
        for (int n = 0; n < d.Signals.Count; n++)
            if (d.Signals[n].Key == "loss_streak") stopped = true;
        T.Ok(!stopped, "so a green day with no run in it is not called a stop");

        // And the run it IS for: three straight losers ends the day.
        BallastTracker r = new BallastTracker();
        r.Config = new TrackerConfig();
        r.Config.StartingBalance = 250000;
        r.Config.TrailingDrawdown = 6500;
        r.Config.DailyLossLimit = 9000;
        r.Config.MaxTrades = 20;
        r.Config.MaxLossesBeforeStop = 3;
        r.Config.TrustAccountRealised = false;
        r.EnsureSession(t0, 0, 250000);
        r.OnEquity(250000, 0);

        realised = 0;
        double[] run = new double[] { 400, -200, -200, -200 };
        for (int n = 0; n < run.Length; n++)
        {
            DateTime at = t0.AddMinutes(n * 10);
            r.OnPosition(1, realised, at, "NQ SEP26", "APEX-11325-105");
            realised += run[n];
            r.OnPosition(0, realised, at.AddMinutes(2), "NQ SEP26", "APEX-11325-105");
            r.OnEquity(250000 + realised, realised);
        }

        T.Eq(r.LossStreak, 3, "three straight losers is a run of three");

        DisciplineInput ri = r.BuildInput(t0.AddHours(1));
        DisciplineDecision rd = DisciplineEngine.Evaluate(ri);
        bool caught = false;
        for (int n = 0; n < rd.Signals.Count; n++)
            if (rd.Signals[n].Key == "loss_streak") caught = true;
        T.Ok(caught, "and that is the shape worth stopping - it still stops him");

        T.Ok(DisciplineEngine.RowWarning(ri, rd).IndexOf("in a row") >= 0,
             "and it now says in a row, which it always claimed to mean");

        List<TiltTrigger> wall = TiltLockout.EvaluateAll("APEX-11325-105", ri, rd, true);
        string line = "";
        for (int n = 0; n < wall.Count; n++)
            if (wall[n].Kind == TiltKind.LossStreak) line = wall[n].Line;
        T.Ok(line.IndexOf("3 losses in a row") >= 0, "the wall says it too: " + line);

        // A winner after the run gives him his day back. That is the point of a
        // streak: it is a state he can trade his way out of, not a budget spent.
        r.OnPosition(1, realised, t0.AddMinutes(50), "NQ SEP26", "APEX-11325-105");
        realised += 300;
        r.OnPosition(0, realised, t0.AddMinutes(52), "NQ SEP26", "APEX-11325-105");
        r.OnEquity(250000 + realised, realised);
        T.Eq(r.LossStreak, 0, "one winner clears it");
    }

    /// <summary>
    /// "it says wait on ballast but on the chart it is done for the day, so that
    /// is misleading"
    ///
    /// Sim101, seven trades of seven, and four minutes past a loss. Both
    /// readings were correct; the cooldown was simply checked first, so the row
    /// said WAIT while the chart said DONE FOR THE DAY.
    ///
    /// WAIT is a promise that something changes if he waits. Nothing changes -
    /// the day is over - so he would have sat out the clock and found the door
    /// still shut. A state he can wait out must never outrank one he cannot.
    /// </summary>
    static void TheRowAndTheChartNeverDisagree()
    {
        T.S("a state you can wait out never outranks one you cannot");

        // His row, to the number.
        DisciplineInput i = Sim101(7, 1);
        i.LastTradeWasLoss = true;

        DisciplineDecision d = DisciplineEngine.Evaluate(i);

        T.Eq(d.Action, DisciplineAction.StopForDay,
             "the day is over, and that is what the column says");
        T.Ok(d.Action != DisciplineAction.Cooldown,
             "not WAIT - waiting changes nothing when the count is spent");
        T.Eq(d.Urgency, Urgency.Alert, "red, matching the chart rather than arguing with it");

        string row = DisciplineEngine.RowWarning(i, d);
        T.Ok(row.IndexOf("the day is done") >= 0, "and the words agree with the column: " + row);
        T.Ok(row.IndexOf("wait it out") < 0, "rather than telling him to wait for nothing");

        // The chart is told the same thing, which is what "lining up" means.
        List<TiltTrigger> wall = TiltLockout.EvaluateAll("Sim101", i, d, true);
        bool hard = false;
        for (int n = 0; n < wall.Count; n++)
            if (TiltLockout.IsHardBreaker(wall[n].Kind)) hard = true;
        T.Ok(hard, "the chart still locks, as it already did");

        // With trades to spare, the cooldown is exactly right and still shows.
        // Built fresh rather than copied - DisciplineInput is a class, and
        // assigning it aliases the original instead of copying it.
        DisciplineInput room = Sim101(3, 1);
        room.LastTradeWasLoss = true;
        DisciplineDecision rd = DisciplineEngine.Evaluate(room);
        T.Eq(rd.Action, DisciplineAction.Cooldown,
             "a cooldown with trades left is still a cooldown");
        T.Ok(DisciplineEngine.RowWarning(room, rd).IndexOf("wait it out") >= 0,
             "and it still says wait, because waiting genuinely helps there");

        // A loss streak already outranked the cooldown and must keep doing so.
        DisciplineInput streak = Sim101(3, 3);
        streak.LastTradeWasLoss = true;
        T.Eq(DisciplineEngine.Evaluate(streak).Action, DisciplineAction.StopForDay,
             "three in a row ends the day whatever the clock says");
    }

    /// <summary>His Sim101 row, four minutes past a loss, with the count varied.</summary>
    /// <summary>
    /// "it says im up 69 and 84 in 2 accounts and you mention protect it,
    /// meaning trade cautiously or not at all..that is no where near my goal
    /// for the day...i think that should only come out when you have reached
    /// half or 2/3rd your target"
    ///
    /// It was saying it the moment the day went a cent green. $69 against a
    /// $250 target is 28% of the way there, and telling a trader to protect
    /// 28% of his goal is telling him to stop before he has started.
    ///
    /// Below the line the row says the number and stops. Advice on every line
    /// teaches a trader to read past all of it, and then the lines that matter
    /// get read past too.
    /// </summary>
    static void ProtectItWaitsUntilThereIsSomethingToProtect()
    {
        T.S("protect it waits until there is something to protect");

        // His own row: $69 on APEX-11325-105, whose target is $250.
        DisciplineInput early = Green(69, 250);
        string row = DisciplineEngine.RowWarning(early, DisciplineEngine.Evaluate(early));
        T.Eq(row, "green $69", "at 28% of target it states the number and says nothing else");

        // And the other one, $84 against Sim103's $1,500.
        DisciplineInput other = Green(84, 1500);
        T.Eq(DisciplineEngine.RowWarning(other, DisciplineEngine.Evaluate(other)),
             "green $84", "6% of a target is not a day worth protecting");

        // Just under two-thirds - still nothing.
        DisciplineInput under = Green(166, 250);
        T.Eq(DisciplineEngine.RowWarning(under, DisciplineEngine.Evaluate(under)),
             "green $166", "a dollar short of the line is still short of it");

        // At two-thirds it speaks.
        DisciplineInput at = Green(167, 250);
        T.Eq(DisciplineEngine.RowWarning(at, DisciplineEngine.Evaluate(at)),
             "green $167 - protect it", "two-thirds of the way there is worth protecting");

        // At the target the existing line takes over and says more than this one.
        DisciplineInput hit = Green(250, 250);
        T.Eq(DisciplineEngine.RowWarning(hit, DisciplineEngine.Evaluate(hit)),
             "target hit - bank it or free-roll, do not give it back",
             "and the target itself still has its own words");

        // No target set, no judgement to make. "Protect it" is a statement
        // about how much of the day's goal is on the table, and without a goal
        // there is nothing to measure it against.
        DisciplineInput noTarget = Green(900, 0);
        T.Eq(DisciplineEngine.RowWarning(noTarget, DisciplineEngine.Evaluate(noTarget)),
             "green $900", "an account with no target is never told to protect anything");

        // A red day is untouched by any of this.
        DisciplineInput red = Green(-120, 250);
        T.Eq(DisciplineEngine.RowWarning(red, DisciplineEngine.Evaluate(red)),
             "clear", "and a day that is down reads as it always did");
    }

    /// <summary>A clean green day on an account with nothing else to say about it.</summary>
    static DisciplineInput Green(double dayPnl, double target)
    {
        DisciplineInput i = new DisciplineInput();
        i.StartingBalance = 250000; i.TrailingDrawdown = 6500;
        i.CurrentEquity = 247683; i.HasValidEquity = true;
        i.FloorLevel = 243500; i.CushionToFloor = 4183;
        i.MaxTrades = 5; i.TradesToday = 1;
        i.MaxLossesBeforeStop = 3; i.LossStreak = 0;
        i.DailyLossLimit = 250; i.DailyTarget = target;
        i.MaxContracts = 4; i.BaseMaxContracts = 4;
        i.NowMinuteEt = 690; i.SessionStartMinute = 570; i.SessionEndMinute = 750;
        i.MinutesSinceLastLoss = 120; i.CooldownMinutes = 15;
        i.DailyPnl = dayPnl;
        i.PeakDailyPnl = dayPnl > 0 ? dayPnl : 0;
        return i;
    }

    static DisciplineInput Sim101(int tradesToday, int streak)
    {
        DisciplineInput i = new DisciplineInput();
        i.StartingBalance = 100000; i.TrailingDrawdown = 6500;
        i.CurrentEquity = 100049; i.HasValidEquity = true;
        i.FloorLevel = 95021; i.CushionToFloor = 5028;
        i.MaxTrades = 7; i.TradesToday = tradesToday;
        i.MaxLossesBeforeStop = 3; i.LossStreak = streak;
        i.DailyLossLimit = 1000; i.DailyPnl = 49; i.DailyTarget = 1200;
        i.MaxContracts = 4; i.NowMinuteEt = 690;
        i.MinutesSinceLastLoss = 4; i.CooldownMinutes = 15;
        return i;
    }

    static FirmAccountSpec Spec(string firm, string label, double size, double dd,
                                double firmDaily, double lockAt, int cap)
    {
        FirmAccountSpec s = new FirmAccountSpec();
        s.Firm = firm;
        s.Plan = label;
        s.Size = size;
        s.Drawdown = dd;
        s.DrawdownType = DrawdownType.Intraday;
        s.DailyLossLimit = firmDaily;
        s.LockFloorAt = lockAt;
        s.ProfitTarget = 0;
        s.FirmMaxContracts = cap;
        return s;
    }

    // ── all four limits are per account ──────────────────────────────────────

    static void FourLimitsDivergePerAccount()
    {
        T.S("four different limits on four accounts");

        BallastMonitor m = new BallastMonitor();

        BallastTracker a = m.GetOrCreate("APEX-105");
        BallastTracker b = m.GetOrCreate("APEX-106");
        BallastTracker c = m.GetOrCreate("APEX-109");

        a.Config.DailyLossLimit = 500;  a.Config.DailyTarget = 750;
        a.Config.MaxTrades = 4;         a.Config.MaxLossesBeforeStop = 2;

        b.Config.DailyLossLimit = 1200; b.Config.DailyTarget = 2000;
        b.Config.MaxTrades = 8;         b.Config.MaxLossesBeforeStop = 3;

        c.Config.DailyLossLimit = 250;  c.Config.DailyTarget = 300;
        c.Config.MaxTrades = 2;         c.Config.MaxLossesBeforeStop = 1;

        T.Near(m.Get("APEX-105").Config.DailyLossLimit, 500, 0.01, "105 keeps its own daily loss");
        T.Near(m.Get("APEX-106").Config.DailyLossLimit, 1200, 0.01, "106 keeps a different one");
        T.Near(m.Get("APEX-109").Config.DailyLossLimit, 250, 0.01, "109 keeps a third");

        T.Near(m.Get("APEX-105").Config.DailyTarget, 750, 0.01, "and its own target");
        T.Near(m.Get("APEX-106").Config.DailyTarget, 2000, 0.01, "different per account");

        T.Eq(m.Get("APEX-105").Config.MaxTrades, 4, "its own trade count");
        T.Eq(m.Get("APEX-106").Config.MaxTrades, 8, "different per account");
        T.Eq(m.Get("APEX-109").Config.MaxTrades, 2, "and a third again");

        T.Eq(m.Get("APEX-105").Config.MaxLossesBeforeStop, 2, "its own loss streak");
        T.Eq(m.Get("APEX-106").Config.MaxLossesBeforeStop, 3, "different per account");
        T.Eq(m.Get("APEX-109").Config.MaxLossesBeforeStop, 1, "and a third again");

        // Un-ticking one must not disturb the others, and must not disturb its
        // own numbers either.
        m.Remove("APEX-106");
        TrackerConfig kept = m.RememberedConfig("APEX-106");
        T.Ok(kept != null, "an un-ticked account keeps its rules");
        T.Near(kept.DailyLossLimit, 1200, 0.01, "including its daily loss");
        T.Eq(kept.MaxTrades, 8, "its trade count");
        T.Eq(kept.MaxLossesBeforeStop, 3, "and its loss streak");
        T.Near(m.Get("APEX-105").Config.DailyLossLimit, 500, 0.01,
               "and the accounts still watched are untouched");

        BallastTracker back = m.GetOrCreate("APEX-106");
        T.Near(back.Config.DailyLossLimit, 1200, 0.01, "re-ticking hands all four back");
        T.Eq(back.Config.MaxTrades, 8, "trade count included");
        T.Eq(back.Config.MaxLossesBeforeStop, 3, "loss streak included");
        T.Near(back.Config.DailyTarget, 2000, 0.01, "target included");
    }

    // ── the rule book stops overwriting the trader's decision ────────────────

    static void RuleBookKeepsTheTradersOwnDailyStop()
    {
        T.S("choosing an account type does not erase the daily stop");

        TrackerConfig mine = new TrackerConfig();
        mine.DailyLossLimit = 500;
        mine.DailyTarget = 750;
        mine.MaxTrades = 4;
        mine.MaxLossesBeforeStop = 2;
        mine.MaxContracts = 4;
        mine.BaseMaxContracts = 4;

        // Apex publishes no daily loss limit at all. This used to write 0 over
        // the 500 and leave the account running with nothing.
        FirmAccountSpec apex = Spec("Apex Trader Funding", "Legacy evaluation (Rithmic)",
                                    250000, 6500, 0, 265000, 27);

        TrackerConfig after = RuleBook.ToConfig(apex, mine);

        T.Near(after.DailyLossLimit, 500, 0.01,
               "the firm publishing none does not mean the trader chose none");
        T.Near(after.FirmDailyLossLimit, 0, 0.01, "and the firm's absence is recorded as absence");
        T.Near(after.DailyTarget, 750, 0.01, "the target is the trader's and stays");
        T.Eq(after.MaxTrades, 4, "so does the trade count");
        T.Eq(after.MaxLossesBeforeStop, 2, "so does the loss streak");
        T.Eq(after.MaxContracts, 4, "and their size, which is under the firm's 27");
        T.Eq(after.FirmMaxContracts, 27, "with the firm's own cap recorded separately");

        // The firm's facts still land.
        T.Near(after.StartingBalance, 250000, 0.01, "the size comes from the firm");
        T.Near(after.TrailingDrawdown, 6500, 0.01, "and the drawdown");
        T.Near(after.LockFloorAt, 265000, 0.01, "and where the floor stops trailing");

        // Two accounts of the SAME type, set up differently, must stay different
        // after both are pointed at that type. This is the trader's actual case.
        TrackerConfig one = new TrackerConfig();
        one.DailyLossLimit = 500; one.MaxTrades = 4; one.MaxLossesBeforeStop = 2;
        TrackerConfig two = new TrackerConfig();
        two.DailyLossLimit = 1200; two.MaxTrades = 8; two.MaxLossesBeforeStop = 3;

        TrackerConfig oneAfter = RuleBook.ToConfig(apex, one);
        TrackerConfig twoAfter = RuleBook.ToConfig(apex, two);

        T.Near(oneAfter.DailyLossLimit, 500, 0.01, "same firm, same plan, first account's stop");
        T.Near(twoAfter.DailyLossLimit, 1200, 0.01, "and the second account's own");
        T.Eq(oneAfter.MaxTrades, 4, "first account's trade count");
        T.Eq(twoAfter.MaxTrades, 8, "second account's trade count");
        T.Eq(oneAfter.MaxLossesBeforeStop, 2, "first account's loss streak");
        T.Eq(twoAfter.MaxLossesBeforeStop, 3, "second account's loss streak");
    }

    static void FirmDailyLimitStillBinds()
    {
        T.S("a firm that does publish a daily limit still wins");

        // Topstep-style: the firm has a hard daily loss limit. Breaching it ends
        // the account, so it is a ceiling on the trader's own number.
        FirmAccountSpec firm = Spec("Some Firm", "50K", 50000, 2000, 1000, 50000, 5);

        TrackerConfig loose = new TrackerConfig();
        loose.DailyLossLimit = 1500;   // looser than the firm allows
        TrackerConfig tight = new TrackerConfig();
        tight.DailyLossLimit = 400;    // tighter than the firm requires
        TrackerConfig none = new TrackerConfig();
        none.DailyLossLimit = 0;       // never set one

        T.Near(RuleBook.ToConfig(firm, loose).DailyLossLimit, 1000, 0.01,
               "a limit looser than the firm's is brought down to the firm's");
        T.Near(RuleBook.ToConfig(firm, tight).DailyLossLimit, 400, 0.01,
               "a tighter one is the trader's business and is left alone");
        T.Near(RuleBook.ToConfig(firm, none).DailyLossLimit, 1000, 0.01,
               "and an account with none set gets the firm's");

        T.Near(RuleBook.ToConfig(firm, tight).FirmDailyLossLimit, 1000, 0.01,
               "the firm's own figure is recorded either way, so it can be shown");

        // Contracts behave the same way and always did - this is only here so
        // that if one of the two ever changes, the pair is compared.
        TrackerConfig big = new TrackerConfig();
        big.MaxContracts = 20; big.BaseMaxContracts = 20;
        T.Eq(RuleBook.ToConfig(firm, big).MaxContracts, 5, "size is capped at the firm's");
    }

    // ── and all of it survives a restart ─────────────────────────────────────

    static void LimitsSurviveTheSettingsFile()
    {
        T.S("per-account limits survive a restart");

        TrackerConfig a = new TrackerConfig();
        a.DailyLossLimit = 500;  a.DailyTarget = 750;
        a.MaxTrades = 4;         a.MaxLossesBeforeStop = 2;
        a.FirmDailyLossLimit = 0;

        TrackerConfig b = new TrackerConfig();
        b.DailyLossLimit = 1200; b.DailyTarget = 2000;
        b.MaxTrades = 8;         b.MaxLossesBeforeStop = 3;
        b.FirmDailyLossLimit = 1500;

        string key;
        TrackerConfig a2 = SettingsCodec.Deserialise(SettingsCodec.Serialise("APEX-105", a), out key);
        T.Eq(key, "APEX-105", "the account name comes back");
        T.Near(a2.DailyLossLimit, 500, 0.01, "and its daily loss");
        T.Near(a2.DailyTarget, 750, 0.01, "and its target");
        T.Eq(a2.MaxTrades, 4, "and its trade count");
        T.Eq(a2.MaxLossesBeforeStop, 2, "and its loss streak");

        TrackerConfig b2 = SettingsCodec.Deserialise(SettingsCodec.Serialise("APEX-106", b), out key);
        T.Near(b2.DailyLossLimit, 1200, 0.01, "the second account's are still its own");
        T.Near(b2.DailyTarget, 2000, 0.01, "target");
        T.Eq(b2.MaxTrades, 8, "trade count");
        T.Eq(b2.MaxLossesBeforeStop, 3, "loss streak");
        T.Near(b2.FirmDailyLossLimit, 1500, 0.01, "and the firm's own limit is remembered too");

        // A file written by the previous build has 22 fields and no firm daily
        // limit. It must load, and what it holds must be read as the TRADER's
        // limit - the safe reading, because that is the one that keeps stopping
        // them.
        string old = string.Join("|", new string[] {
            "APEX-105", "250000", "6500", "0", "2", "500", "750", "4", "4",
            "265000", "", "0", "0", "0", "4", "0", "1", "570", "690", "5", "27", "15000"
        });
        TrackerConfig loaded = SettingsCodec.Deserialise(old, out key);
        T.Ok(loaded != null, "a file from the previous build still loads");
        T.Near(loaded.DailyLossLimit, 500, 0.01, "with the daily loss intact");
        T.Eq(loaded.MaxTrades, 4, "the trade count intact");
        T.Eq(loaded.MaxLossesBeforeStop, 2, "the loss streak intact");
        T.Near(loaded.FirmDailyLossLimit, 0, 0.01,
               "and no firm limit invented for it");
    }

    // ── the numbers have to actually do something ────────────────────────────

    static void EngineActsOnEachAccountsOwnNumbers()
    {
        T.S("each account is stopped by its own limits");

        DateTime t0 = new DateTime(2026, 8, 3, 9, 40, 0);
        BallastMonitor m = new BallastMonitor();

        BallastTracker a = m.GetOrCreate("APEX-105");
        a.Config.StartingBalance = 100000;
        a.Config.TrailingDrawdown = 6500;
        a.Config.DailyLossLimit = 500;
        a.Config.MaxTrades = 4;
        a.Config.MaxLossesBeforeStop = 2;

        BallastTracker b = m.GetOrCreate("APEX-106");
        b.Config.StartingBalance = 100000;
        b.Config.TrailingDrawdown = 6500;
        b.Config.DailyLossLimit = 1200;
        b.Config.MaxTrades = 8;
        b.Config.MaxLossesBeforeStop = 3;

        a.EnsureSession(t0, 0, 100000); a.OnEquity(100000, 0);
        b.EnsureSession(t0, 0, 100000); b.OnEquity(100000, 0);

        // Four trades on each, all winners so only the count can bite.
        double pnl = 0;
        for (int n = 0; n < 4; n++)
        {
            DateTime at = t0.AddMinutes(n * 10);
            a.OnPosition(1, pnl, at, "NQ SEP26", "APEX-105");
            b.OnPosition(1, pnl, at, "NQ SEP26", "APEX-106");
            pnl += 100;
            a.OnPosition(0, pnl, at.AddMinutes(2), "NQ SEP26", "APEX-105");
            b.OnPosition(0, pnl, at.AddMinutes(2), "NQ SEP26", "APEX-106");
        }

        DisciplineDecision da = DisciplineEngine.Evaluate(a.BuildInput(t0.AddHours(1)));
        DisciplineDecision db = DisciplineEngine.Evaluate(b.BuildInput(t0.AddHours(1)));

        T.Eq(da.Action, DisciplineAction.StopForDay,
             "the account that said four trades is done at four");
        T.Ok(db.Action != DisciplineAction.StopForDay,
             "the account that said eight is not - same day, same trades, different rule");

        // And the daily loss limits bite at different depths.
        BallastTracker c = m.GetOrCreate("APEX-109");
        c.Config.StartingBalance = 100000;
        c.Config.TrailingDrawdown = 6500;
        c.Config.DailyLossLimit = 250;
        c.Config.MaxTrades = 20;
        c.Config.MaxLossesBeforeStop = 9;
        c.EnsureSession(t0, 0, 100000); c.OnEquity(100000, 0);

        c.OnPosition(1, 0, t0, "NQ SEP26", "APEX-109");
        c.OnPosition(0, -300, t0.AddMinutes(2), "NQ SEP26", "APEX-109");
        c.OnEquity(99700, -300);

        DisciplineDecision dc = DisciplineEngine.Evaluate(c.BuildInput(t0.AddHours(1)));
        T.Eq(dc.Action, DisciplineAction.Lockout,
             "$300 down locks out the account that said $250");

        BallastTracker d = m.GetOrCreate("APEX-110");
        d.Config.StartingBalance = 100000;
        d.Config.TrailingDrawdown = 6500;
        d.Config.DailyLossLimit = 1200;
        d.Config.MaxTrades = 20;
        d.Config.MaxLossesBeforeStop = 9;
        d.EnsureSession(t0, 0, 100000); d.OnEquity(100000, 0);

        d.OnPosition(1, 0, t0, "NQ SEP26", "APEX-110");
        d.OnPosition(0, -300, t0.AddMinutes(2), "NQ SEP26", "APEX-110");
        d.OnEquity(99700, -300);

        DisciplineDecision dd = DisciplineEngine.Evaluate(d.BuildInput(t0.AddHours(1)));
        T.Ok(dd.Action != DisciplineAction.Lockout,
             "and does not lock out the account that said $1,200");
    }
}
