using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// Two things a trader asked about on 3 August.
///
/// "it says in the indicator outside trading window... where do we set that" -
/// nowhere, was the answer. It was hard-coded at 09:30-11:30, so anybody who
/// trades the afternoon or the overnight session was told every day that they
/// were breaking a rule they had never chosen.
///
/// "i dont want that left today to get larger if im winning... im just making
/// sure the left today doesnt calculate all the losses seperately" - both
/// halves of that, pinned.
/// </summary>
public static class WindowTests
{
    public static void Run()
    {
        NoWindowMeansSilence();
        OrdinaryWindow();
        WindowAcrossMidnight();
        ParsingTimes();
        WindowSurvivesTheSettingsFile();
        RoomIsNetAndNeverGrows();
    }

    // ── the window ───────────────────────────────────────────────────────────

    static void NoWindowMeansSilence()
    {
        T.S("no trading window set");

        // The default. A brand new account must never be told it is trading at
        // the wrong time, because it has not been told when the right time is.
        TrackerConfig fresh = new TrackerConfig();
        T.Eq(fresh.SessionStartMinute, fresh.SessionEndMinute,
             "a new account starts with no window at all");

        T.Ok(DisciplineEngine.InSessionWindow(0, 0, 0), "midnight is inside no window");
        T.Ok(DisciplineEngine.InSessionWindow(14 * 60, 0, 0), "so is the afternoon");
        T.Ok(DisciplineEngine.InSessionWindow(23 * 60 + 59, 0, 0), "so is one minute to midnight");
        T.Ok(DisciplineEngine.InSessionWindow(9 * 60 + 30, 780, 780),
             "and a window with both ends the same is no window either");

        T.Eq(DisciplineEngine.WindowLabel(0, 0), "any time", "and it says so");

        // And the signal really does stay quiet.
        DisciplineInput i = new DisciplineInput();
        i.NowMinuteEt = 15 * 60;         // 15:00, well outside the old hard-coded window
        i.SessionStartMinute = 0;
        i.SessionEndMinute = 0;
        i.HasValidEquity = true;
        i.CushionToFloor = 5000;
        i.MaxContracts = 2;

        List<RiskSignal> signals = DisciplineEngine.DetectRiskSignals(i);
        T.Ok(!HasKey(signals, "out_of_window"),
             "3pm with no window set produces no complaint about the clock");
    }

    static void OrdinaryWindow()
    {
        T.S("an ordinary trading window");

        int start = 9 * 60 + 30;   // 09:30
        int end = 11 * 60 + 30;    // 11:30

        T.Ok(!DisciplineEngine.InSessionWindow(9 * 60 + 29, start, end), "a minute early is outside");
        T.Ok(DisciplineEngine.InSessionWindow(start, start, end), "the opening minute is inside");
        T.Ok(DisciplineEngine.InSessionWindow(10 * 60, start, end), "the middle is inside");
        T.Ok(DisciplineEngine.InSessionWindow(end, start, end), "the closing minute is inside");
        T.Ok(!DisciplineEngine.InSessionWindow(11 * 60 + 31, start, end), "a minute late is outside");

        T.Eq(DisciplineEngine.WindowLabel(start, end), "09:30-11:30", "and it reads back properly");

        DisciplineInput i = new DisciplineInput();
        i.NowMinuteEt = 15 * 60;
        i.SessionStartMinute = start;
        i.SessionEndMinute = end;
        i.HasValidEquity = true;
        i.CushionToFloor = 5000;
        i.MaxContracts = 2;

        List<RiskSignal> signals = DisciplineEngine.DetectRiskSignals(i);
        T.Ok(HasKey(signals, "out_of_window"),
             "3pm against a morning window is still flagged - the warning is not gone, just chosen");
    }

    static void WindowAcrossMidnight()
    {
        T.S("a window that crosses midnight");

        int start = 18 * 60;       // 18:00
        int end = 2 * 60;          // 02:00

        // Read literally, "after 18:00 and before 02:00" is empty, and every
        // hour of the day used to fail it.
        T.Ok(DisciplineEngine.InSessionWindow(19 * 60, start, end), "seven in the evening is inside");
        T.Ok(DisciplineEngine.InSessionWindow(23 * 60 + 59, start, end), "so is just before midnight");
        T.Ok(DisciplineEngine.InSessionWindow(0, start, end), "so is midnight itself");
        T.Ok(DisciplineEngine.InSessionWindow(60, start, end), "so is one in the morning");
        T.Ok(DisciplineEngine.InSessionWindow(end, start, end), "so is the closing minute");
        T.Ok(!DisciplineEngine.InSessionWindow(2 * 60 + 1, start, end), "two minutes past two is outside");
        T.Ok(!DisciplineEngine.InSessionWindow(12 * 60, start, end), "and so is midday");

        T.Eq(DisciplineEngine.WindowLabel(start, end), "18:00-02:00", "and it reads back properly");
    }

    static void ParsingTimes()
    {
        T.S("reading the times a trader types");

        T.Eq(DisciplineEngine.ParseHourMinute("09:30"), 570, "09:30");
        T.Eq(DisciplineEngine.ParseHourMinute("9:30"), 570, "9:30");
        T.Eq(DisciplineEngine.ParseHourMinute(" 9:30 "), 570, "with spaces");
        T.Eq(DisciplineEngine.ParseHourMinute("930"), 570, "930");
        T.Eq(DisciplineEngine.ParseHourMinute("0930"), 570, "0930");
        T.Eq(DisciplineEngine.ParseHourMinute("9.30"), 570, "9.30");
        T.Eq(DisciplineEngine.ParseHourMinute("9"), 540, "a bare hour");
        T.Eq(DisciplineEngine.ParseHourMinute("16"), 960, "a bare afternoon hour");
        T.Eq(DisciplineEngine.ParseHourMinute("00:00"), 0, "midnight");
        T.Eq(DisciplineEngine.ParseHourMinute("23:59"), 1439, "the last minute of the day");

        // A typo must be REFUSED, not silently turned into midnight - that would
        // put a trader outside their own window all day with no clue why.
        T.Eq(DisciplineEngine.ParseHourMinute(""), -1, "empty is refused");
        T.Eq(DisciplineEngine.ParseHourMinute(null), -1, "so is nothing at all");
        T.Eq(DisciplineEngine.ParseHourMinute("half nine"), -1, "so is words");
        T.Eq(DisciplineEngine.ParseHourMinute("25:00"), -1, "so is an hour that does not exist");
        T.Eq(DisciplineEngine.ParseHourMinute("09:75"), -1, "so is a minute that does not exist");
        T.Eq(DisciplineEngine.ParseHourMinute("-3"), -1, "so is a negative");

        // Round trip.
        for (int m = 0; m < 1440; m += 7)
            T.Eq(DisciplineEngine.ParseHourMinute(DisciplineEngine.HourMinute(m)), m,
                 m == 0 ? "every minute of the day round trips" : "");
    }

    static void WindowSurvivesTheSettingsFile()
    {
        T.S("the window is per account and survives a restart");

        BallastMonitor m = new BallastMonitor();

        BallastTracker morning = m.GetOrCreate("APEX-105");
        morning.Config.SessionStartMinute = 9 * 60 + 30;
        morning.Config.SessionEndMinute = 11 * 60 + 30;

        BallastTracker overnight = m.GetOrCreate("APEX-106");
        overnight.Config.SessionStartMinute = 18 * 60;
        overnight.Config.SessionEndMinute = 2 * 60;

        BallastTracker anyTime = m.GetOrCreate("APEX-109");
        anyTime.Config.SessionStartMinute = 0;
        anyTime.Config.SessionEndMinute = 0;

        T.Eq(m.Get("APEX-105").Config.SessionEndMinute, 690, "the morning account keeps its window");
        T.Eq(m.Get("APEX-106").Config.SessionStartMinute, 1080, "the overnight account keeps its own");
        T.Eq(m.Get("APEX-109").Config.SessionEndMinute, 0, "and the third has none");

        string key;
        TrackerConfig back = SettingsCodec.Deserialise(
            SettingsCodec.Serialise("APEX-106", overnight.Config), out key);
        T.Eq(back.SessionStartMinute, 1080, "a window across midnight saves");
        T.Eq(back.SessionEndMinute, 120, "and loads");
        T.Ok(DisciplineEngine.InSessionWindow(23 * 60, back.SessionStartMinute, back.SessionEndMinute),
             "and still means what it meant");

        TrackerConfig none = SettingsCodec.Deserialise(
            SettingsCodec.Serialise("APEX-109", anyTime.Config), out key);
        T.Eq(none.SessionStartMinute, none.SessionEndMinute, "and \"any time\" survives as \"any time\"");
    }

    // ── what is left to lose today ───────────────────────────────────────────

    static void RoomIsNetAndNeverGrows()
    {
        T.S("what is left to lose today");

        DateTime t0 = new DateTime(2026, 8, 3, 9, 40, 0);

        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.DailyLossLimit = 2500;
        t.Config.MaxTrades = 20;
        t.Config.MaxLossesBeforeStop = 9;
        t.EnsureSession(t0, 0, 100000);
        t.OnEquity(100000, 0);

        // Win 300, lose 200, lose 300, win 100. Two losers totalling $500, but
        // the day is only $100 down - and $100 is what today has cost.
        double realised = 0;
        double[] trades = new double[] { 300, -200, -300, 100 };
        for (int n = 0; n < trades.Length; n++)
        {
            DateTime at = t0.AddMinutes(n * 10);
            t.OnPosition(1, realised, at, "NQ SEP26", "APEX-105");
            realised += trades[n];
            t.OnPosition(0, realised, at.AddMinutes(2), "NQ SEP26", "APEX-105");
            t.OnEquity(100000 + realised, realised);
        }

        DisciplineInput i = t.BuildInput(t0.AddHours(1));
        T.Eq(i.LossesToday, 2, "two of those trades lost");
        T.Near(i.DailyPnl, -100, 0.01,
               "but the day is 100 down, not 500 - losers only ever count net against winners");
        T.Near(Room(i), 2400, 0.01,
               "so 2,400 of the 2,500 budget is left, not 2,000");

        // A green day must not enlarge the budget.
        DisciplineInput up = new DisciplineInput();
        up.DailyLossLimit = 2500;
        up.DailyPnl = 900;
        T.Near(Room(up), 2500, 0.01,
               "up 900 leaves 2,500 to lose, not 3,400 - profit does not top up the budget");

        up.DailyPnl = 5000;
        T.Near(Room(up), 2500, 0.01, "and a very good day does not either");

        up.DailyPnl = 0;
        T.Near(Room(up), 2500, 0.01, "flat leaves the whole budget");

        up.DailyPnl = -2500;
        T.Near(Room(up), 0, 0.01, "and it runs out exactly where the rule fires");

        up.DailyPnl = -4000;
        T.Near(Room(up), 0, 0.01, "past that it is spent, never negative");

        // The display and the rule have to agree at the moment it bites, or the
        // trader is told they have room by one part of the window and stopped by
        // another.
        DisciplineInput bite = new DisciplineInput();
        bite.DailyLossLimit = 2500;
        bite.DailyPnl = -2500;
        bite.HasValidEquity = true;
        bite.CushionToFloor = 4000;
        bite.MaxContracts = 2;
        DisciplineDecision d = DisciplineEngine.Evaluate(bite);
        T.Eq(d.Action, DisciplineAction.Lockout, "the rule fires when the budget reads zero");

        DisciplineInput green = new DisciplineInput();
        green.DailyLossLimit = 2500;
        green.DailyPnl = 900;
        green.HasValidEquity = true;
        green.CushionToFloor = 4000;
        green.MaxContracts = 2;
        T.Ok(DisciplineEngine.Evaluate(green).Action != DisciplineAction.Lockout,
             "and not while the day is green");

        // No limit set is not a budget of zero.
        DisciplineInput noLimit = new DisciplineInput();
        noLimit.DailyLossLimit = 0;
        noLimit.DailyPnl = -900;
        T.Near(Room(noLimit), 0, 0.01, "with no limit set there is no budget to report");
    }

    /// <summary>
    /// Mirrors BallastWindow.RoomToday, which cannot be reached from here
    /// because it lives in the WPF layer. If one changes, this must.
    /// </summary>
    static double Room(DisciplineInput i)
    {
        if (i == null || i.DailyLossLimit <= 0) return 0;
        double room = i.DailyLossLimit + i.DailyPnl;
        if (room > i.DailyLossLimit) room = i.DailyLossLimit;
        return room < 0 ? 0 : room;
    }

    static bool HasKey(List<RiskSignal> signals, string key)
    {
        for (int n = 0; n < signals.Count; n++) if (signals[n].Key == key) return true;
        return false;
    }
}
