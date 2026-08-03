using System;
using System.IO;
using Ballast;

/// <summary>
/// Provider-reported trailing room is the zero-input path for an existing PA.
/// These cases pin the conservative bounds and restart behaviour that make the
/// value safe enough to use for a liquidation threshold.
/// </summary>
public static class ProviderFloorTests
{
    public static void Run()
    {
        ExistingPaBelowTriggerIsRecovered();
        InvalidProviderValueIsRejected();
        FirmThresholdNeverMovesDown();
        ProviderThresholdSurvivesRestart();
        LegacyApex150KLocksAt150100();
        RuleFallbackRemainsConservative();
        EndOfDayProviderFloorOverridesOnlyUpward();
        ApexPaProfilesResolveWithoutNumberEntry();
    }

    static TrackerConfig Current50K(DrawdownType type)
    {
        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 50000;
        c.TrailingDrawdown = 2000;
        c.DrawdownType = type;
        c.LockFloorAt = 50100;
        return c;
    }

    static BallastTracker Open(TrackerConfig c, double equity)
    {
        BallastTracker t = new BallastTracker();
        t.Config = c;
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        t.EnsureSession(now, 0, equity);
        t.OnEquity(equity, 0, equity);
        return t;
    }

    static void ExistingPaBelowTriggerIsRecovered()
    {
        T.S("an existing PA floor comes from the connected firm account");
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker t = Open(Current50K(DrawdownType.Intraday), 51000);

        T.Ok(t.ObserveProviderTrailingRoom(900, 51000, now),
             "NinjaTrader's 900 remaining implies the locked 50,100 threshold");
        DisciplineInput i = t.BuildInput(now);
        T.Near(i.FloorLevel, 50100, 0.01, "the provider floor wins over an incomplete local peak");
        T.Near(i.CushionToFloor, 900, 0.01, "the displayed room matches the provider");
        T.Ok(i.FirmFloorProviderConfirmed, "the UI can say this came from the firm account");
        T.Ok(i.FloorLocked, "and recognizes that trailing has stopped");
    }

    static void InvalidProviderValueIsRejected()
    {
        T.S("an impossible provider floor is ignored");
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker t = Open(Current50K(DrawdownType.Intraday), 51000);

        T.Ok(!t.ObserveProviderTrailingRoom(100, 51000, now),
             "a derived threshold above Apex's published lock cannot enter state");
        T.Near(t.BuildInput(now).FloorLevel, 49000, 0.01,
               "the conservative rule-derived fallback remains intact");
        T.Ok(!t.ObserveProviderTrailingRoom(0, 51000, now),
             "an unsupported zero is not mistaken for no remaining room");
    }

    static void FirmThresholdNeverMovesDown()
    {
        T.S("a firm-reported threshold is monotonic");
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker t = Open(Current50K(DrawdownType.Intraday), 51000);

        T.Ok(t.ObserveProviderTrailingRoom(1500, 51000, now), "49,500 is accepted");
        T.Ok(t.ObserveProviderTrailingRoom(2000, 51000, now.AddSeconds(1)),
             "a later valid report is readable");
        T.Near(t.AuthoritativeFirmFloor, 49500, 0.01,
               "but a threshold that has risen never moves back down");
    }

    static void ProviderThresholdSurvivesRestart()
    {
        T.S("a provider-confirmed floor survives restart");
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker restored = Open(Current50K(DrawdownType.Intraday), 51000);
        restored.SeedRiskState(now, 51000, 50000, 51000, 50100, true);

        DisciplineInput i = restored.BuildInput(now);
        T.Near(i.FloorLevel, 50100, 0.01, "the saved firm floor is used before a new event arrives");
        T.Ok(i.FirmFloorProviderConfirmed, "its authority is retained");
    }

    static void LegacyApex150KLocksAt150100()
    {
        T.S("legacy Apex 150K PA uses the published 5,000 plus 100 safety net");
        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 150000;
        c.TrailingDrawdown = 5000;
        c.DrawdownType = DrawdownType.Intraday;
        c.LockFloorAt = 150100;
        BallastTracker t = Open(c, 153000);
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);

        T.Ok(t.ObserveProviderTrailingRoom(2900, 153000, now),
             "an older locked PA is resolved without asking for its historical peak");
        T.Near(t.BuildInput(now).FloorLevel, 150100, 0.01,
               "the final floor is starting balance plus 100");
    }

    static void RuleFallbackRemainsConservative()
    {
        T.S("missing provider data falls back to observed history");
        TrackerConfig c = Current50K(DrawdownType.Intraday);
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker beforeLock = Open(c, 51000);
        T.Near(beforeLock.BuildInput(now).FloorLevel, 49000, 0.01,
               "Ballast does not invent a lock it cannot prove");

        BallastTracker proved = Open(c, 52100);
        T.Near(proved.BuildInput(now).FloorLevel, 50100, 0.01,
               "the observed balance itself proves the published lock trigger");
        T.Ok(proved.BuildInput(now).FloorLocked, "and no number entry is required");
    }

    static void EndOfDayProviderFloorOverridesOnlyUpward()
    {
        T.S("provider floor resolves an existing EOD PA");
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);
        BallastTracker t = Open(Current50K(DrawdownType.EndOfDay), 51000);
        T.Ok(t.ObserveProviderTrailingRoom(900, 51000, now), "the EOD account report is accepted");
        T.Near(t.BuildInput(now).FloorLevel, 50100, 0.01,
               "the firm threshold overrides the incomplete completed-session history");
    }

    static void ApexPaProfilesResolveWithoutNumberEntry()
    {
        T.S("Apex PA profiles resolve from account identity");
        RuleBook rules = new RuleBook();
        T.Ok(rules.Load(Path.Combine("Ballast", "ballast-rules.txt")), "the bundled rules load");

        FirmAccountSpec legacy = rules.AutoDetect("PA-APEX-12345-01", 150000, true,
                                                   AccountGeneration.Legacy, "RITHMIC");
        T.Ok(legacy != null && RuleBook.IsLegacyPlanName(legacy.Plan),
             "a stated legacy book selects the legacy PA automatically");
        T.Near(legacy.Drawdown, 5000, 0.01, "with the legacy 150K drawdown");
        T.Near(legacy.LockFloorAt, 150100, 0.01, "and starting balance plus 100 lock floor");

        FirmAccountSpec current = rules.AutoDetect("PA-APEX-12345-02", 150000, true,
                                                    AccountGeneration.Current, "RITHMIC");
        T.Near(current.Drawdown, 4000, 0.01, "the current 150K PA gets the current drawdown");
        T.Near(current.LockFloorAt, 150100, 0.01, "without asking the trader for a floor");

        FirmAccountSpec safest = rules.AutoDetect("PA-APEX-12345-03", 150000, true,
                                                   AccountGeneration.Auto, "RITHMIC");
        T.Near(safest.Drawdown, 4000, 0.01,
               "an ambiguous generation takes the tighter published drawdown");
    }
}
