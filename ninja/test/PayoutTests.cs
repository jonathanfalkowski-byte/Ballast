using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// The consistency rule, which is the only prop rule that punishes a good day.
///
/// "A $600 day on a $1,000 total balance blocks you."
///
/// It does - at 50%. At 30%, which is what his own 250K legacy accounts run,
/// a $600 day needs $2,000 behind it. The arithmetic is the firm's own, and
/// these cases are worked from Apex's published examples.
/// </summary>
public static class PayoutTests
{
    public static void Run()
    {
        TheFirmsOwnExample();
        TheCeilingIsWhatTodayMayStillEarn();
        LosingDaysCountAgainstTheTotal();
        CommissionComesOffBecauseTheFirmCountsNet();
        NoDaysUnderneathMeansNoCeilingWorthShowing();
        AFirmWithNoPublishedTermsSaysSoRatherThanGuessing();
        DaysAreGroupedByTheFirmsTradingDay();
        ThirtyAndFiftyAreDifferentAnswersToTheSameDay();
        TheShippedRuleBookKnowsHisAccounts();
        ThePayoutBaselineSurvivesARestart();
        TheRowCountsDownToTheCeiling();
        PastTheCeilingIsAdviceNotALockout();
        TerminalStatesStillOutrankIt();
        AnAccountWithNoTermsIsNeverToldToStop();
        TheCeilingReachesTheAccountRow();
        AnEvaluationHasNoPayoutToProtect();
    }

    static PayoutRules Legacy()
    {
        PayoutRules r = new PayoutRules();
        r.ConsistencyPct = 30;
        r.QualifyingDayMinimum = 50;
        r.QualifyingDaysRequired = 5;
        r.MinimumPayout = 500;
        r.MaxPayouts = 6;
        return r;
    }

    static PayoutRules Current()
    {
        PayoutRules r = Legacy();
        r.ConsistencyPct = 50;
        r.QualifyingDayMinimum = 250;
        return r;
    }

    static List<PayoutDay> D(params double[] pnls)
    {
        List<PayoutDay> days = new List<PayoutDay>();
        DateTime d = new DateTime(2026, 7, 1);
        for (int i = 0; i < pnls.Length; i++)
        {
            PayoutDay p = new PayoutDay();
            p.Day = d.AddDays(i);
            p.Pnl = pnls[i];
            days.Add(p);
        }
        return days;
    }

    /// <summary>
    /// Apex: "If your highest profit day was $1,500, you need at least $5,000
    /// total profit" - 1,500 ÷ 0.3.
    /// </summary>
    static void TheFirmsOwnExample()
    {
        T.S("the firm's own worked example");

        DateTime today = new DateTime(2026, 8, 12);

        // $1,500 best day, $3,000 total. 1,500/3,000 = 50%, over the 30% line.
        PayoutStanding s = PayoutBook.Stand(D(1500, 700, 500, 200, 100), today, 0, Legacy());

        T.Near(s.NetProfit, 3000, 0.01, "three thousand banked");
        T.Near(s.BestDay, 1500, 0.01, "with a fifteen hundred dollar day in it");
        T.Near(s.Share, 0.5, 0.001, "which is half of it");
        T.Ok(s.Blocked, "so a payout requested now is refused");
        T.Near(s.ProfitToUnblock, 2000, 0.01,
               "and 1,500 divided by 0.3 is 5,000 - two thousand more than he has");

        // Add the two thousand and it clears.
        PayoutStanding ok = PayoutBook.Stand(D(1500, 700, 500, 200, 100, 900, 1100), today, 0, Legacy());
        T.Near(ok.NetProfit, 5000, 0.01, "five thousand total");
        T.Ok(!ok.Blocked, "clears the rule exactly at the firm's own figure");
        T.Ok(ok.CouldRequestNow, "and with seven qualifying days he can ask");
    }

    /// <summary>
    /// The number he can act on while the day is still open.
    /// </summary>
    static void TheCeilingIsWhatTodayMayStillEarn()
    {
        T.S("what today may still earn");

        DateTime today = new DateTime(2026, 8, 12);

        // $2,000 banked across four days, none of them large. At 30% today may
        // make 0.3 x 2000 / 0.7 = 857.14 before it becomes the windfall.
        List<PayoutDay> banked = D(500, 500, 500, 500);

        PayoutStanding flat = PayoutBook.Stand(banked, today, 0, Legacy());
        T.Near(flat.CeilingToday, 857.14, 0.01, "today may earn 857 before it blocks him");
        T.Ok(flat.CeilingWorthShowing, "and there is a payout worth protecting");
        T.Ok(!flat.PastCeiling, "nothing earned yet, so nothing crossed");

        // A day just under it is fine.
        PayoutStanding under = PayoutBook.Stand(banked, today, 850, Legacy());
        T.Ok(!under.Blocked, "850 on top of 2,000 is 29.8% - inside the rule");
        T.Ok(!under.PastCeiling, "and inside the ceiling");

        // A day just over it is not - and the ceiling itself does not move,
        // because it is built from the other days.
        PayoutStanding over = PayoutBook.Stand(banked, today, 900, Legacy());
        T.Ok(over.PastCeiling, "900 is past it");
        T.Ok(over.Blocked, "and the payout is deferred");
        T.Near(over.CeilingToday, 857.14, 0.01, "the ceiling is not moved by crossing it");
        T.Near(over.ProfitToUnblock, 100, 0.01,
               "900 ÷ 0.3 is 3,000, and he has 2,900 - a hundred short");

        // Which is the point of the whole thing: stopping at the ceiling keeps
        // the payout, and the difference is a hundred dollars of profit against
        // a withdrawal he can make today.
        T.Ok(under.CouldRequestNow || under.DaysStillNeeded > 0,
             "stopping short leaves him able to ask, or only short of days");
    }

    static void LosingDaysCountAgainstTheTotal()
    {
        T.S("losing days are in the total");

        DateTime today = new DateTime(2026, 8, 12);

        // Apex: "net profit in the account is used to calculate consistency".
        // 1,000 best day, 1,200 gross of winners, 400 given back = 800 net.
        PayoutStanding s = PayoutBook.Stand(D(1000, 200, -400), today, 0, Legacy());

        T.Near(s.NetProfit, 800, 0.01, "the losing day comes off the total");
        T.Near(s.BestDay, 1000, 0.01, "but never off the best day");
        T.Ok(s.Blocked, "so a day bigger than the whole net profit certainly blocks");
        T.Near(s.ProfitToUnblock, 2533.33, 0.01, "1,000 ÷ 0.3 is 3,333, less the 800 he has");

        // And a losing day is not a qualifying day.
        T.Eq(s.QualifyingDays, 2, "two days cleared the fifty dollar minimum");
        T.Eq(s.DaysStillNeeded, 3, "three more to go");
    }

    /// <summary>
    /// The journal records what the trade made; the account records what it
    /// made after commission. The firm counts the account's figure.
    /// </summary>
    static void CommissionComesOffBecauseTheFirmCountsNet()
    {
        T.S("the firm counts net, so Ballast does");

        List<BallastTrade> all = new List<BallastTrade>();
        all.Add(Trade("Sim103", new DateTime(2026, 8, 12, 9, 31, 0), 230, 4.36));
        all.Add(Trade("Sim103", new DateTime(2026, 8, 12, 9, 37, 0), -350, 4.36));
        all.Add(Trade("Other",  new DateTime(2026, 8, 12, 9, 40, 0), 5000, 0));

        List<PayoutDay> days = PayoutBook.Days(all, "Sim103", DateTime.MinValue.Date, 0);

        T.Eq(days.Count, 1, "one trading day");
        T.Near(days[0].Pnl, -128.72, 0.01,
               "230 less 350 less 8.72 of commission - what the account actually says");
    }

    static void NoDaysUnderneathMeansNoCeilingWorthShowing()
    {
        T.S("day one after a payout has no ceiling to give");

        DateTime today = new DateTime(2026, 8, 12);

        // Freshly paid out. Nothing banked. Any winning day is 100% of profit,
        // so "stop at zero" is arithmetically true and useless as advice.
        PayoutStanding s = PayoutBook.Stand(new List<PayoutDay>(), today, 800, Legacy());

        T.Ok(!s.CeilingWorthShowing, "so no ceiling is put on screen");
        T.Ok(!s.PastCeiling, "and he is not told he has crossed one");
        T.Eq(s.DaysStillNeeded, 4, "what he actually needs is four more days");
        T.Ok(!s.CouldRequestNow, "and he could not ask yet anyway");
    }

    static void AFirmWithNoPublishedTermsSaysSoRatherThanGuessing()
    {
        T.S("a firm with no published terms gets no borrowed ones");

        DateTime today = new DateTime(2026, 8, 12);
        PayoutStanding s = PayoutBook.Stand(D(1500, 100), today, 0, new PayoutRules());

        T.Ok(!s.Known, "nothing is claimed");
        T.Ok(!s.Blocked, "no rule is invented");
        T.Near(s.CeilingToday, 0, 0.01, "and no ceiling is offered");
    }

    static void DaysAreGroupedByTheFirmsTradingDay()
    {
        T.S("an overnight session is one day, not two");

        List<BallastTrade> all = new List<BallastTrade>();
        // 17:00 reset. A trade at 18:00 Monday and one at 02:00 Tuesday are the
        // same session, and both belong to Monday.
        all.Add(Trade("A", new DateTime(2026, 8, 10, 18, 0, 0), 300, 0));
        all.Add(Trade("A", new DateTime(2026, 8, 11, 2, 0, 0), 200, 0));
        all.Add(Trade("A", new DateTime(2026, 8, 11, 18, 0, 0), 100, 0));

        List<PayoutDay> days = PayoutBook.Days(all, "A", DateTime.MinValue.Date, 17 * 60);
        T.Eq(days.Count, 2, "two sessions, not three days");
        T.Near(days[0].Pnl, 500, 0.01, "the overnight half belongs to the session that opened it");

        // And the baseline cuts days off cleanly.
        List<PayoutDay> since = PayoutBook.Days(all, "A", new DateTime(2026, 8, 11), 17 * 60);
        T.Eq(since.Count, 1, "days before the last payout are not counted again");
    }

    /// <summary>
    /// The reason the percentage is read from the rule book rather than typed
    /// into the source: it is 30 on his accounts and 50 on most people's, and
    /// the same day is fine under one and blocking under the other.
    /// </summary>
    static void ThirtyAndFiftyAreDifferentAnswersToTheSameDay()
    {
        T.S("thirty and fifty disagree about the same day");

        DateTime today = new DateTime(2026, 8, 12);
        List<PayoutDay> banked = D(700, 700, 600);      // 2,000 banked

        PayoutStanding legacy = PayoutBook.Stand(banked, today, 1000, Legacy());
        PayoutStanding current = PayoutBook.Stand(banked, today, 1000, Current());

        T.Ok(legacy.Blocked, "a 1,000 day on 2,000 banked is 33% - blocked at 30");
        T.Ok(!current.Blocked, "and the same day is 33% - allowed at 50");
        T.Near(legacy.CeilingToday, 857.14, 0.01, "the legacy ceiling is 857");
        T.Near(current.CeilingToday, 2000, 0.01, "the current one is 2,000");
    }

    /// <summary>
    /// The reason this is in the rule book rather than the source: his own two
    /// funded accounts are 250K, Apex 4.0 stops at 150K, so they are legacy and
    /// they run 30% - not the 50% every article about Apex quotes.
    /// </summary>
    static void TheShippedRuleBookKnowsHisAccounts()
    {
        T.S("the shipped rule book knows which percentage is his");

        RuleBook rb = new RuleBook();
        T.Ok(rb.Load("Ballast/ballast-rules.txt"), "the shipped rule book loads");

        PayoutRules legacy = rb.PayoutFor("Apex Trader Funding", "Legacy PA / funded", 250000);
        T.Ok(legacy.Known, "a 250K Apex account has published payout terms");
        T.Near(legacy.ConsistencyPct, 30, 0.01, "and they are the 30% legacy ones");
        T.Near(legacy.QualifyingDayMinimum, 50, 0.01, "fifty dollars makes a day count");
        T.Eq(legacy.QualifyingDaysRequired, 5, "five days to a payout");
        T.Near(legacy.MinimumPayout, 500, 0.01, "five hundred minimum");

        // Size 0 covers the whole legacy plan, so every legacy size answers.
        T.Near(rb.PayoutFor("Apex Trader Funding", "Legacy PA / funded", 75000).ConsistencyPct,
               30, 0.01, "and a 75K legacy account gets the same rule");

        // 4.0 accounts are 50%, and the qualifying minimum is per size AND
        // differs between intraday and end-of-day.
        PayoutRules intraday = rb.PayoutFor("Apex Trader Funding", "PA / funded (intraday)", 100000);
        T.Near(intraday.ConsistencyPct, 50, 0.01, "a 4.0 100K runs 50%");
        T.Near(intraday.QualifyingDayMinimum, 250, 0.01, "with a $250 qualifying day intraday");
        T.Near(rb.PayoutFor("Apex Trader Funding", "PA / funded (end-of-day)", 100000).QualifyingDayMinimum,
               300, 0.01, "and $300 on the end-of-day version - not a typo");

        // A firm whose payout terms nobody has read gets nothing.
        T.Ok(!rb.PayoutFor("Topstep", "Express Funded", 50000).Known,
             "a firm with no PAYOUT line reports no terms rather than Apex's");
        T.Ok(!rb.PayoutFor("Made Up Prop", "Anything", 50000).Known,
             "and so does a firm that is not in the book at all");
    }

    /// <summary>
    /// The one thing Ballast cannot work out for itself, so the one thing that
    /// must come back exactly after a restart.
    /// </summary>
    static void ThePayoutBaselineSurvivesARestart()
    {
        T.S("the payout baseline survives a restart");

        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 250000;
        c.TrailingDrawdown = 6500;
        c.LastPayoutOn = new DateTime(2026, 7, 28);
        c.PayoutsTaken = 3;

        string key;
        TrackerConfig back = SettingsCodec.Deserialise(SettingsCodec.Serialise("APEX-11325-105", c), out key);

        T.Eq(key, "APEX-11325-105", "the account comes back");
        T.Eq(back.LastPayoutOn, new DateTime(2026, 7, 28), "and so does the date it was last paid");
        T.Eq(back.PayoutsTaken, 3, "and how many times");

        // Never paid out round-trips as never paid out, not as 1 January.
        TrackerConfig never = new TrackerConfig();
        TrackerConfig neverBack = SettingsCodec.Deserialise(SettingsCodec.Serialise("Sim101", never), out key);
        T.Eq(neverBack.LastPayoutOn, DateTime.MinValue.Date, "never paid stays never paid");
        T.Eq(neverBack.PayoutsTaken, 0, "with no payouts behind it");

        // A settings file written before any of this existed still loads, and
        // reads as never paid out - which counts the whole journal, the right
        // answer for an account nobody has withdrawn from.
        string old27 = string.Join("|", new string[] {
            "APEX-11325-106", "250000", "6500", "0", "2", "2000", "1400", "5", "4",
            "265000", "", "0", "0", "0", "4", "0", "0", "570", "750", "5", "27",
            "15000", "0", "1", "0", "0", "0"
        });
        TrackerConfig older = SettingsCodec.Deserialise(old27, out key);
        T.Ok(older != null, "an older settings line still loads");
        T.Eq(older.LastPayoutOn, DateTime.MinValue.Date, "and reads as never paid out");
        T.Near(older.StartingBalance, 250000, 0.01, "with everything it did know intact");
        T.Near(older.TrailingDrawdown, 6500, 0.01, "including the drawdown");
    }

    /// <summary>An account 2,000 up since its last payout, on the 30% rule.</summary>
    static DisciplineInput Apex105(double dayPnl)
    {
        DisciplineInput i = new DisciplineInput();
        i.StartingBalance = 250000; i.TrailingDrawdown = 6500;
        i.CurrentEquity = 250000 + dayPnl; i.HasValidEquity = true;
        i.FloorLevel = 243500; i.CushionToFloor = 6500 + dayPnl;
        i.MaxTrades = 5; i.TradesToday = 2;
        i.MaxLossesBeforeStop = 3; i.LossStreak = 0;
        i.DailyLossLimit = 250; i.DailyTarget = 250;
        i.MaxContracts = 4; i.NowMinuteEt = 690;
        i.MinutesSinceLastLoss = 90; i.CooldownMinutes = 15;
        i.SessionStartMinute = 570; i.SessionEndMinute = 750;
        i.DailyPnl = dayPnl; i.PeakDailyPnl = dayPnl > 0 ? dayPnl : 0;

        i.ConsistencyPct = 30;
        i.WindfallCeiling = 857.14;
        i.ProfitToUnblockPayout = 0;
        return i;
    }

    /// <summary>
    /// The number he can act on. The row already said "green $600 - protect
    /// it"; now it says how much of that is left before protecting it means
    /// something quite different.
    /// </summary>
    static void TheRowCountsDownToTheCeiling()
    {
        T.S("the row counts down to the ceiling");

        DisciplineInput i = Apex105(600);
        i.DailyTarget = 0;                       // keep the target advice out of the way
        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        string row = DisciplineEngine.RowWarning(i, d);

        T.Ok(row.IndexOf("257", StringComparison.Ordinal) >= 0,
             "857 less the 600 already made is 257 more before it holds up a payout, got: " + row);
        T.Ok(row.IndexOf("payout", StringComparison.Ordinal) >= 0, "and it says what for");

        // A day with no ceiling in play reads exactly as it always did.
        DisciplineInput plain = Apex105(600);
        plain.DailyTarget = 0;
        plain.WindfallCeiling = 0; plain.ConsistencyPct = 0;
        T.Eq(DisciplineEngine.RowWarning(plain, DisciplineEngine.Evaluate(plain)),
             "green $600", "and an account with no published terms says only the number");
    }

    /// <summary>
    /// Nothing is lost when this trips, so it never becomes the kind of wall
    /// that has to be typed out of.
    /// </summary>
    static void PastTheCeilingIsAdviceNotALockout()
    {
        T.S("past the ceiling is advice, not a lockout");

        DisciplineInput i = Apex105(900);
        i.DailyTarget = 0;
        i.PastWindfallCeiling = true;
        i.ProfitToUnblockPayout = 100;

        DisciplineDecision d = DisciplineEngine.Evaluate(i);

        T.Eq(d.Action, DisciplineAction.ProtectGreen, "the advice is to bank it");
        T.Eq(d.Urgency, Urgency.Caution,
             "at caution, not alert - the alert states are the ones where money is leaving");

        string row = DisciplineEngine.RowWarning(i, d);
        T.Ok(row.IndexOf("past the", StringComparison.Ordinal) >= 0, "the row says he is past it: " + row);
        T.Ok(row.IndexOf("100", StringComparison.Ordinal) >= 0, "and what it now costs to clear");

        // There is a wall, and it is a soft one.
        List<TiltTrigger> walls = TiltLockout.EvaluateAll("APEX-11325-105", i, d, false);
        bool found = false;
        for (int n = 0; n < walls.Count; n++)
            if (walls[n].Kind == TiltKind.Windfall) found = true;
        T.Ok(found, "the wall appears");
        T.Ok(!TiltLockout.IsHardBreaker(TiltKind.Windfall),
             "but it is not a hard breaker - it never locks him out of a live trade");

        // And it cannot fire on a day that is not green.
        DisciplineInput red = Apex105(-400);
        red.PastWindfallCeiling = true;
        DisciplineDecision rd = DisciplineEngine.Evaluate(red);
        List<TiltTrigger> none = TiltLockout.EvaluateAll("APEX-11325-105", red, rd, false);
        for (int n = 0; n < none.Count; n++)
            T.Ok(none[n].Kind != TiltKind.Windfall, "a red day never raises a payout ceiling");
    }

    /// <summary>
    /// A day that is both past the ceiling and past the trade count is a day
    /// that is over. The row must say the thing he cannot wait out.
    /// </summary>
    static void TerminalStatesStillOutrankIt()
    {
        T.S("a day that is over says so first");

        DisciplineInput i = Apex105(900);
        i.PastWindfallCeiling = true;
        i.ProfitToUnblockPayout = 100;
        i.TradesToday = 5;                       // at the limit

        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        T.Eq(d.Action, DisciplineAction.StopForDay, "the day being over outranks the paperwork");

        string row = DisciplineEngine.RowWarning(i, d);
        T.Ok(row.IndexOf("trades", StringComparison.Ordinal) >= 0,
             "and the row says the trade count, not the ceiling: " + row);
    }

    static void AnAccountWithNoTermsIsNeverToldToStop()
    {
        T.S("no published terms, no ceiling");

        DisciplineInput i = Apex105(5000);
        i.DailyTarget = 0;
        i.WindfallCeiling = 0;
        i.ConsistencyPct = 0;
        i.PastWindfallCeiling = true;            // even if something set it

        DisciplineDecision d = DisciplineEngine.Evaluate(i);
        List<TiltTrigger> walls = TiltLockout.EvaluateAll("Sim110", i, d, false);
        for (int n = 0; n < walls.Count; n++)
            T.Ok(walls[n].Kind != TiltKind.Windfall, "no ceiling means no wall");
        T.Ok(DisciplineEngine.RowWarning(i, d).IndexOf("payout", StringComparison.Ordinal) < 0,
             "and the row never mentions a payout rule it does not have");
    }

    /// <summary>
    /// End to end, through the one door every figure goes through: rule book,
    /// journal, monitor, discipline row. The arithmetic being right in
    /// isolation is worth nothing if it never reaches the screen.
    /// </summary>
    static void TheCeilingReachesTheAccountRow()
    {
        T.S("the ceiling reaches the account row");

        RuleBook rb = new RuleBook();
        T.Ok(rb.Load("Ballast/ballast-rules.txt"), "the shipped rule book loads");

        BallastMonitor m = new BallastMonitor();
        m.Rules = rb;

        // A 250K Apex account, which is legacy, which means 30%.
        BallastTracker t = m.GetOrCreate("APEX-11325-105");
        t.Config.StartingBalance = 250000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.DrawdownType = DrawdownType.Intraday;
        t.Config.LockFloorAt = 250100;
        t.Config.DailyTarget = 0;
        t.Config.DailyLossLimit = 0;
        t.Config.MaxTrades = 0;
        t.Config.MaxLossesBeforeStop = 0;
        t.Config.SessionStartMinute = 0;
        t.Config.SessionEndMinute = 0;
        t.Config.TrustAccountRealised = false;

        // Four earlier days, $500 each, none of them a windfall.
        DateTime today = new DateTime(2026, 8, 12);
        for (int d = 0; d < 4; d++)
        {
            DateTime when = today.AddDays(-(4 - d)).AddHours(10);
            m.Journal.Add(Trade("APEX-11325-105", when, 500, 0));
        }

        // Today opens and runs to +600.
        t.EnsureSession(today.AddHours(9).AddMinutes(30), 0, 252000);
        t.OnEquity(252000, 0, 252000);
        t.OnEquity(252600, 600, 252600);

        AccountSnapshot s = m.Evaluate("APEX-11325-105", today.AddHours(10));
        T.Ok(s != null, "the account evaluates");
        T.Near(s.Input.DailyPnl, 600, 0.01, "today is up 600");
        T.Near(s.Input.ConsistencyPct, 30, 0.01, "on the 30% rule the rule book gave it");
        T.Near(s.Input.WindfallCeiling, 857.14, 0.01, "with an 857 ceiling from the four days behind it");
        T.Ok(!s.Input.PastWindfallCeiling, "not past it yet");

        string row = DisciplineEngine.RowWarning(s.Input, s.Decision);
        T.Ok(row.IndexOf("257", StringComparison.Ordinal) >= 0,
             "and the row says 257 more, got: " + row);

        // Push it past.
        t.OnEquity(253000, 1000, 253000);
        AccountSnapshot over = m.Evaluate("APEX-11325-105", today.AddHours(11));
        T.Ok(over.Input.PastWindfallCeiling, "a 1,000 day is past the ceiling");
        T.Eq(over.Decision.Action, DisciplineAction.ProtectGreen, "and the advice is to bank it");
        T.Near(over.Input.ProfitToUnblockPayout, 333.33, 0.01,
               "1,000 divided by 0.3 is 3,333, against the 3,000 he would have");

        // A practice account never gets a ceiling, whatever the journal says.
        BallastTracker sim = m.GetOrCreate("Sim101");
        sim.Config.StartingBalance = 250000;
        sim.Config.TrailingDrawdown = 6500;
        sim.Config.TrustAccountRealised = false;
        for (int d = 0; d < 4; d++)
            m.Journal.Add(Trade("Sim101", today.AddDays(-(4 - d)).AddHours(10), 500, 0));
        sim.EnsureSession(today.AddHours(9).AddMinutes(30), 0, 252000);
        sim.OnEquity(253000, 1000, 253000);

        AccountSnapshot simSnap = m.Evaluate("Sim101", today.AddHours(11));
        T.Near(simSnap.Input.WindfallCeiling, 0, 0.01, "a practice account has no payout to protect");
        T.Ok(!simSnap.Input.PastWindfallCeiling, "and is never told it has crossed one");
    }

    /// <summary>
    /// "just so you know none of those accounts are PA all are evals"
    ///
    /// An evaluation cannot be withdrawn from, so it has no consistency rule to
    /// break, and a ceiling on one would be advice to stop trading for no
    /// reason at all.
    ///
    /// Two locks, because the first one has failed before. The rule book gets
    /// there on its own - a 250K Apex account with a floor that fixes at
    /// 265,000 is a Legacy EVALUATION, not the Legacy PA that shares its size
    /// and drawdown, and evaluation plans have no PAYOUT line. The second is
    /// the account's stated purpose, for the day an account is set up as the
    /// wrong type. 109 and 110 have worn a 250K's drawdown on a 100K body.
    /// </summary>
    static void AnEvaluationHasNoPayoutToProtect()
    {
        T.S("an evaluation has no payout to protect");

        RuleBook rb = new RuleBook();
        T.Ok(rb.Load("Ballast/ballast-rules.txt"), "the shipped rule book loads");

        // His own settings line for 105, exactly as it sits on disk.
        string key;
        TrackerConfig c = SettingsCodec.Deserialise(
            "APEX-11325-105|250000|6500|0|3|250|250|5|4|265000||0|0|0|4|0|0|570|750|5|27|15000|0|1|0|0|0",
            out key);

        FirmAccountSpec spec = rb.MatchSpecForAccount(key, c);
        T.Ok(spec != null, "the rule book recognises it");
        T.Ok(spec.Plan.IndexOf("valuation", StringComparison.Ordinal) >= 0,
             "as an evaluation, not a funded account - got: " + spec.Plan);
        T.Ok(!rb.PayoutForAccount(key, c).Known,
             "so it has no payout terms and can never be given a ceiling");

        // The second lock: the same account wearing the wrong type.
        BallastMonitor m = new BallastMonitor();
        m.Rules = rb;
        BallastTracker t = m.GetOrCreate("APEX-11325-105");
        t.Config = c;
        t.Config.Purpose = AccountPurpose.Evaluation;
        t.Config.LockFloorAt = 250100;              // now matching the Legacy PA row
        t.Config.ProfitTarget = 0;
        t.Config.DailyTarget = 0;
        t.Config.DailyLossLimit = 0;
        t.Config.MaxTrades = 0;
        t.Config.MaxLossesBeforeStop = 0;
        t.Config.SessionStartMinute = 0;
        t.Config.SessionEndMinute = 0;
        t.Config.TrustAccountRealised = false;

        T.Ok(rb.PayoutForAccount("APEX-11325-105", t.Config).Known,
             "the rule book would now hand it the funded account's terms");

        DateTime today = new DateTime(2026, 8, 12);
        for (int d = 0; d < 4; d++)
            m.Journal.Add(Trade("APEX-11325-105", today.AddDays(-(4 - d)).AddHours(10), 500, 0));
        t.EnsureSession(today.AddHours(9).AddMinutes(30), 0, 252000);
        t.OnEquity(253000, 1000, 253000);

        AccountSnapshot s = m.Evaluate("APEX-11325-105", today.AddHours(11));
        T.Near(s.Input.WindfallCeiling, 0, 0.01,
               "but an account he has said is an evaluation is never given one");
        T.Ok(!s.Input.PastWindfallCeiling, "and is never told it has crossed one");

        // And when it does become a PA, the same account gets the ceiling.
        t.Config.Purpose = AccountPurpose.Funded;
        AccountSnapshot funded = m.Evaluate("APEX-11325-105", today.AddHours(11));
        T.Near(funded.Input.ConsistencyPct, 30, 0.01,
               "a 250K PA is legacy, so 30% - the day the eval passes, this wakes up");
        T.Near(funded.Input.WindfallCeiling, 857.14, 0.01, "with the ceiling its four days earn it");
    }

    static BallastTrade Trade(string account, DateTime exit, double pnl, double commission)
    {
        BallastTrade t = new BallastTrade();
        t.AccountName = account;
        t.EntryTime = exit.AddMinutes(-1);
        t.ExitTime = exit;
        t.Pnl = pnl;
        t.Commission = commission;
        return t;
    }
}
