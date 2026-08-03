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
