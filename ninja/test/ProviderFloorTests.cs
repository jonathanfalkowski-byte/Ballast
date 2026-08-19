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
        HisOwn106AgainstRithmic();
    }

    /// <summary>
    /// "the account says it is at for apex 106 it is at 245782.34 and the
    /// account in the ballast on the chart says i have 2184 is that
    /// correct?...i feel i should have less room but maybe not?"
    ///
    /// He was right. Rithmic put the threshold at 244,246.02 and Ballast at
    /// 243,602.04 - $644 of room that did not exist, in the one direction this
    /// software must never be wrong in.
    ///
    /// Neither figure was miscalculated. A trailing floor hangs off the
    /// account's high-water mark; the firm computes that on every tick and
    /// Ballast computes it from what NinjaTrader hands it. The firm caught a
    /// peak of 250,746.02 that Ballast never saw, and a missed peak always
    /// means a floor too low, which always means too much room.
    ///
    /// These are his real numbers, to the cent.
    /// </summary>
    static void HisOwn106AgainstRithmic()
    {
        T.S("his 106 against what Rithmic says");

        DateTime now = new DateTime(2026, 8, 19, 10, 5, 0);

        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 250000;
        c.TrailingDrawdown = 6500;
        c.DrawdownType = DrawdownType.Intraday;
        c.LockFloorAt = 265000;
        c.TrustAccountRealised = false;

        BallastTracker t = new BallastTracker();
        t.Config = c;
        t.EnsureSession(now.Date.AddHours(9), 0, 250000);

        // The peak Ballast actually saw, then the balance it is at now.
        t.OnEquity(250102.04, 0, 250102.04);
        t.OnEquity(245782.34, -1379.36, 245782.34);

        DisciplineInput own = t.BuildInput(now);
        T.Near(own.FloorLevel, 243602.04, 0.01, "Ballast's own floor is peak less the drawdown");
        T.Near(own.CushionToFloor, 2180.30, 0.01, "which is the $2,184 he was shown");
        T.Ok(!own.FloorIsTheFirmsOwn, "and it is not marked as the firm's number, because it is not");

        // Now the figure off R|Trader.
        c.FirmFloorLevel = 244246.02;
        DisciplineInput firm = t.BuildInput(now);
        T.Near(firm.FloorLevel, 244246.02, 0.01, "the firm's threshold is used");
        T.Near(firm.CushionToFloor, 1536.32, 0.01, "and the room is what Rithmic says: 1,536.32");
        T.Ok(firm.FloorIsTheFirmsOwn, "marked as the firm's own on the row");
        T.Near(own.CushionToFloor - firm.CushionToFloor, 643.98, 0.01,
               "the difference is the $644 of room that was never there");

        // It can only ever make Ballast MORE careful. A stale figure below
        // Ballast's own is simply overtaken.
        c.FirmFloorLevel = 243000;
        DisciplineInput stale = t.BuildInput(now);
        T.Near(stale.FloorLevel, 243602.04, 0.01, "a stale low figure loses to Ballast's own");
        T.Ok(!stale.FloorIsTheFirmsOwn, "and the row stops claiming it is the firm's");

        // Nonsense is refused rather than believed. Below the account's
        // starting floor cannot be a threshold for this account.
        c.FirmFloorLevel = 100;
        T.Near(t.BuildInput(now).FloorLevel, 243602.04, 0.01, "an impossible figure is ignored");

        // Above the level the drawdown stops trailing is clamped to it.
        c.FirmFloorLevel = 999999;
        T.Near(t.BuildInput(now).FloorLevel, 243602.04, 0.01,
               "and one beyond the lock level cannot invent a floor either");

        // Zero means what it always meant.
        c.FirmFloorLevel = 0;
        T.Near(t.BuildInput(now).FloorLevel, 243602.04, 0.01, "zero is 'not supplied'");

        // ---- it has to come back after a restart, or he types it every day
        c.FirmFloorLevel = 244246.02;
        string key;
        TrackerConfig back = SettingsCodec.Deserialise(
            SettingsCodec.Serialise("APEX-11325-106", c), out key);
        T.Near(back.FirmFloorLevel, 244246.02, 0.005, "the typed threshold survives a restart");

        // And a settings file written before this field existed still loads.
        string old29 = string.Join("|", new string[] {
            "APEX-11325-106", "250000", "6500", "0", "2", "2000", "1400", "5", "4",
            "265000", "", "0", "0", "0", "4", "0", "0", "570", "750", "5", "27",
            "15000", "0", "1", "0", "0", "0", "", "0"
        });
        TrackerConfig older = SettingsCodec.Deserialise(old29, out key);
        T.Ok(older != null, "an older settings line still loads");
        T.Near(older.FirmFloorLevel, 0, 0.005, "with no firm threshold, as it had none");
        T.Near(older.TrailingDrawdown, 6500, 0.01, "and everything it did know intact");
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
