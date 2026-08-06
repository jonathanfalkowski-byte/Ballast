using System;
using System.Collections.Generic;
using System.IO;
using Ballast;

/// <summary>
/// Cover for the second batch of reports: the chart showing nothing that ever
/// changed, the Now table hiding the trade count, and the new stop/target
/// question in the journal.
/// </summary>
public static class CountTests
{
    public static void Run()
    {
        ChartShowsTheCount();
        MovedQuestion();
        MovedCountsForSomething();
    }

    // ── the chart has to show something that moves ───────────────────────────

    static void ChartShowsTheCount()
    {
        T.S("the chart count");

        DateTime now = new DateTime(2026, 7, 31, 10, 0, 0);
        BallastState.Clear("Sim110");

        BallastState.Publish("Sim110", "clear", 0, "Clear to trade", 6500, true, now);
        BallastState.PublishCount("Sim110", 2, 5, 1, 3, 2400, 3000, -600, 750, now);

        AccountState s = BallastState.Get("Sim110", now);
        T.Ok(s != null, "the account is on the board");

        string line = BallastState.ChartCount(s, "Sim110");
        T.Ok(line.IndexOf("SIM110") >= 0, "the count names the account");
        T.Ok(line.IndexOf("2/5 TRADES") >= 0, "it shows trades taken against the limit");
        T.Ok(line.IndexOf("1/3 LOSSES") >= 0, "it shows losses in a row against the limit");
        // Left of WHAT. "$1,200 LEFT" sat immediately before the target figure,
        // both in dollars, both four digits - "i thought that was my target then
        // realized it is right beside it". A line read sideways in a second while
        // a position is on cannot make the reader work it out from context.
        T.Ok(line.IndexOf("$2,400 LEFT TO LOSE") >= 0, "it says what is left of today's budget, and of what");

        // "OF" not a slash, so the target cannot be read as another count like
        // the trades ratio earlier in the same line.
        T.Ok(line.IndexOf("$0 OF $750 TARGET") >= 0, "and today's target, which is the other end of the same decision");
        T.Ok(line.IndexOf("/$750") < 0, "with only one ratio on the line, and it belongs to the counts");
        T.Ok(line.IndexOf("$6,500 TO FLOOR") >= 0, "and the room to the floor");

        // A chart panel is one line wide and does not ellipsis - it wraps, and
        // a wrapped status strip clips its own left edge. The account name is
        // what gets given up, because the Chart Trader names it an inch away.
        T.Ok(line.Length <= 92, "and the whole line fits one chart row: " + line.Length + " chars");

        // With every field populated and a real prop account number in front of
        // it, the line still has to fit - that is the case that wrapped on his
        // screen and came back as "PEX-11325-106 ... TO THE / LOOR".
        AccountState full = new AccountState();
        full.TradesToday = 3; full.MaxTrades = 5;
        full.LossesToday = 1; full.MaxLosses = 2;
        full.RoomToday = 2000; full.DailyLossLimit = 2000;
        full.DailyPnl = 876; full.DailyTarget = 1400;
        full.CanLose = 5178; full.HasCushion = true;
        string wide = BallastState.ChartCount(full, "APEX-11325-106");
        T.Ok(wide.Length <= 92, "a full line with a prop account number fits: " + wide.Length);
        T.Ok(wide.IndexOf("LEFT TO LOSE") >= 0, "and keeps the words that carry the meaning");
        T.Ok(wide.IndexOf("APEX") < 0, "giving up the account name, which the Chart Trader shows anyway");

        // The actual complaint: changing an account's rules produced no visible
        // change on the chart, because the chart only ever said "BALLAST OK".
        BallastState.PublishCount("Sim110", 2, 8, 1, 3, 2400, 3000, -600, 750, now);
        string after = BallastState.ChartCount(BallastState.Get("Sim110", now), "Sim110");
        T.Ok(after != line, "changing the rules changes what the chart says");
        T.Ok(after.IndexOf("2/8 TRADES") >= 0, "and it shows the new limit");

        // Without limits set it still says something true rather than nothing.
        BallastState.Clear("Sim104");
        BallastState.PublishCount("Sim104", 0, 0, 0, 0, 0, 0, 0, 0, now);
        string bare = BallastState.ChartCount(BallastState.Get("Sim104", now), "Sim104");
        T.Ok(bare.IndexOf("0 TRADES") >= 0, "no trade limit still reports the count");
        T.Ok(bare.IndexOf("LEFT") < 0, "and does not invent a budget that was never set");
        T.Ok(bare.IndexOf("TARGET") < 0, "nor a target that was never set");

        // Singular grammar, because "1 TRADES" on a chart looks broken.
        BallastState.Clear("Sim105");
        BallastState.PublishCount("Sim105", 1, 0, 1, 0, 0, 0, 0, 0, now);
        string one = BallastState.ChartCount(BallastState.Get("Sim105", now), "Sim105");
        T.Ok(one.IndexOf("1 TRADE ") >= 0 || one.EndsWith("1 TRADE"), "one trade reads as a trade");
        T.Ok(one.IndexOf("1 TRADES") < 0, "not as 1 TRADES");
        T.Ok(one.IndexOf("1 LOSSES") < 0, "and one loss reads as a loss");

        // ── The right chart for the right account ───────────────────────────
        //
        // "it is capturing the wrong charts for the trades... apex 106 was on
        // this chart not the one listed in the journal."
        //
        // With NQ open on three charts, every one of them matches the instrument,
        // so the picture came from whichever chart happened to be focused. The
        // Chart Trader account is the only thing that tells them apart.
        List<string> accts = new List<string> { "APEX-11325-105", "APEX-11325-106", "" };
        List<string> instrs = new List<string> { "NQ SEP26", "NQ SEP26", "NQ SEP26" };

        T.Eq(ChartSnapshot.AccountChartIndex(accts, instrs, "APEX-11325-106", "NQ SEP26"), 1,
             "the chart bound to 106 is the one photographed for 106");
        T.Eq(ChartSnapshot.AccountChartIndex(accts, instrs, "APEX-11325-105", "NQ SEP26"), 0,
             "and 105 gets its own");

        // An account on a chart showing something else still beats a chart that
        // is merely in front - but the instrument decides between two of its own.
        List<string> two = new List<string> { "ES 09-26", "NQ SEP26" };
        List<string> both = new List<string> { "APEX-105", "APEX-105" };
        T.Eq(ChartSnapshot.AccountChartIndex(both, two, "APEX-105", "NQ SEP26"), 1,
             "the account's NQ chart wins over the account's ES chart");
        T.Eq(ChartSnapshot.AccountChartIndex(both, two, "APEX-105", "ES 09-26"), 0,
             "and the other way round");

        // Silence rather than a guess.
        T.Eq(ChartSnapshot.AccountChartIndex(accts, instrs, "Sim110", "NQ SEP26"), -1,
             "an account on no chart hands the choice back");
        T.Eq(ChartSnapshot.AccountChartIndex(null, instrs, "APEX-105", "NQ SEP26"), -1,
             "and so does knowing nothing about the charts");
        T.Eq(ChartSnapshot.AccountChartIndex(accts, instrs, "", "NQ SEP26"), -1,
             "and so does not knowing the account");

        // ── Colour by how close, not by whether the day is red ──────────────
        //
        // "Down three dollars" and "two of three losses with $374 of a $3,000
        // budget left" used to be the same shade of white. The engine is right
        // to stay calm until a rule is actually broken, but "not yet broken" and
        // "nowhere near" are different states.
        AccountState clear = new AccountState();
        clear.TradesToday = 1; clear.MaxTrades = 5;
        clear.LossesToday = 0; clear.MaxLosses = 3;
        clear.DailyLossLimit = 3000; clear.RoomToday = 2800;
        T.Eq(BallastState.CountUrgency(clear), 0, "a clean account is clear");

        AccountState thin = new AccountState();
        thin.TradesToday = 3; thin.MaxTrades = 5;
        thin.LossesToday = 2; thin.MaxLosses = 3;
        thin.DailyLossLimit = 3000; thin.RoomToday = 374;
        T.Eq(BallastState.CountUrgency(thin), 1,
             "$374 of a $3,000 budget with two of three losses is not white");

        AccountState budget = new AccountState();
        budget.TradesToday = 1; budget.MaxTrades = 10;
        budget.LossesToday = 0; budget.MaxLosses = 5;
        budget.DailyLossLimit = 3000; budget.RoomToday = 900;
        T.Eq(BallastState.CountUrgency(budget), 1,
             "two thirds of the budget gone is enough on its own");

        AccountState lastLoss = new AccountState();
        lastLoss.TradesToday = 2; lastLoss.MaxTrades = 10;
        lastLoss.LossesToday = 2; lastLoss.MaxLosses = 3;
        T.Eq(BallastState.CountUrgency(lastLoss), 1, "so is one more loss ending the day");

        AccountState lastTrade = new AccountState();
        lastTrade.TradesToday = 4; lastTrade.MaxTrades = 5;
        T.Eq(BallastState.CountUrgency(lastTrade), 1, "and so is one trade left");

        AccountState spent = new AccountState();
        spent.DailyLossLimit = 3000; spent.RoomToday = 0;
        T.Eq(BallastState.CountUrgency(spent), 2, "a spent budget is at a line");

        AccountState stopped = new AccountState();
        stopped.LossesToday = 3; stopped.MaxLosses = 3;
        T.Eq(BallastState.CountUrgency(stopped), 2, "so is the loss streak");

        AccountState counted = new AccountState();
        counted.TradesToday = 5; counted.MaxTrades = 5;
        T.Eq(BallastState.CountUrgency(counted), 2, "so is the trade count");

        // No limits set means nothing to be close to. An account with no rules
        // must never be painted as though it were about to breach one.
        AccountState bareState = new AccountState();
        bareState.TradesToday = 40; bareState.LossesToday = 12;
        T.Eq(BallastState.CountUrgency(bareState), 0, "no limits set is never a warning");
        T.Eq(BallastState.CountUrgency(null), 0, "and neither is nothing at all");

        // The count must not resurrect an account whose window has closed.
        T.Ok(BallastState.Get("Sim110", now.AddMinutes(5)) == null,
             "a stale board still goes stale");

        // A hard breaker still outranks the count entirely.
        BallastState.Clear("Sim110");
        BallastState.Publish("Sim110", "past the daily loss limit - done for the day", 2, "Stop", 0, false, now);
        BallastState.PublishLock("Sim110", true, "You are done for the day.", now);
        T.Ok(BallastState.ChartBanner(BallastState.Get("Sim110", now)).StartsWith("STOP"),
             "the alarm still wins over the count");

        T.Eq(BallastState.ChartCount(null, "Sim110"), "", "a null state produces nothing");
    }

    // ── did you move your stop ───────────────────────────────────────────────

    static void MovedQuestion()
    {
        T.S("the stop/target question");

        T.Ok(BallastJournal.DidMove(BallastJournal.Moved_Stop), "moving the stop counts as moving");
        T.Ok(BallastJournal.DidMove(BallastJournal.Moved_Target), "so does moving the target");
        T.Ok(BallastJournal.DidMove(BallastJournal.Moved_Both), "so does moving both");
        T.Ok(!BallastJournal.DidMove(BallastJournal.Moved_Nothing), "holding both does not");
        T.Ok(!BallastJournal.DidMove(""), "and an unanswered trade does not");
        T.Ok(!BallastJournal.DidMove(null), "nor a null one");

        T.Eq(BallastJournal.MovedLabel(""), "not said", "unanswered has an honest label");
        T.Ok(BallastJournal.MovedLabel(BallastJournal.Moved_Stop).Length > 0, "every option has a label");
        T.Eq(BallastJournal.MovedOptions.Length, 4, "there are four answers");

        // Round trip, including the blank that means "not said".
        BallastTrade e = new BallastTrade();
        e.AccountName = "Sim110";
        e.Instrument = "NQ SEP26";
        e.EntryTime = new DateTime(2026, 7, 31, 9, 40, 0);
        e.ExitTime = new DateTime(2026, 7, 31, 9, 52, 0);
        e.Pnl = -240;
        e.Planned = BallastJournal.Verdict_Sloppy;
        e.Moved = BallastJournal.Moved_Stop;
        e.Note = "widened it because it was about to tag me";

        BallastTrade back = BallastJournal.FromCsvLine(BallastJournal.ToCsvLine(e));
        T.Ok(back != null, "the row parses");
        T.Eq(back.Moved, BallastJournal.Moved_Stop, "the answer survives a save and load");
        T.Eq(back.Note, "widened it because it was about to tag me", "and so does why");

        BallastTrade blank = new BallastTrade();
        blank.AccountName = "Sim110";
        blank.EntryTime = e.EntryTime; blank.ExitTime = e.ExitTime;
        BallastTrade blankBack = BallastJournal.FromCsvLine(BallastJournal.ToCsvLine(blank));
        T.Eq(blankBack.Moved, "", "an unanswered trade stays unanswered");

        T.Ok(BallastJournal.CsvHeader.IndexOf("Moved") >= 0, "the CSV header names the column");
    }

    // ── and it has to be worth answering ─────────────────────────────────────

    static void MovedCountsForSomething()
    {
        T.S("what moving a stop costs");

        BallastJournal j = new BallastJournal();
        DateTime t0 = new DateTime(2026, 7, 31, 9, 30, 0);

        // Four trades where the stop was moved, all losers.
        for (int n = 0; n < 4; n++) j.Add(Trade(t0.AddMinutes(n * 15), -300, BallastJournal.Moved_Stop, false));
        // Six left alone, mostly winners.
        for (int n = 0; n < 6; n++) j.Add(Trade(t0.AddHours(2).AddMinutes(n * 15), 180, BallastJournal.Moved_Nothing, false));
        // Three never answered - these must not be counted as either.
        for (int n = 0; n < 3; n++) j.Add(Trade(t0.AddHours(4).AddMinutes(n * 15), -500, "", false));

        List<JournalBucket> split = j.MovedSplit(j.All);
        T.Eq(split[0].Count, 4, "the moved trades are counted");
        T.Near(split[0].Net, -1200, 0.01, "and costed");
        T.Eq(split[1].Count, 6, "the held trades are counted");
        T.Near(split[1].Net, 1080, 0.01, "and credited");
        T.Ok(split[0].Count + split[1].Count == 10,
             "unanswered trades are counted as neither - a journal must not answer for you");

        string insight = j.HeadlineInsight(j.All, 5, 8);
        T.Ok(insight.IndexOf("moved your stop or target") >= 0,
             "moving stops can become the headline when it is the biggest leak");
        T.Ok(insight.IndexOf("$1,200") >= 0, "quoted with what it actually cost");

        // A bot's trades must never be judged on a question only a human answers.
        BallastJournal bots = new BallastJournal();
        for (int n = 0; n < 20; n++) bots.Add(Trade(t0.AddMinutes(n), -100, BallastJournal.Moved_Stop, true));
        T.Eq(bots.MovedSplit(bots.All)[0].Count, 0, "a strategy's trades are excluded");
    }

    static BallastTrade Trade(DateTime at, double pnl, string moved, bool bot)
    {
        BallastTrade e = new BallastTrade();
        e.AccountName = "Sim110";
        e.Instrument = "NQ SEP26";
        e.EntryTime = at;
        e.ExitTime = at.AddMinutes(6);
        e.Pnl = pnl;
        e.Moved = moved;
        e.Automated = bot;
        e.Planned = BallastJournal.Verdict_ByTheBook;
        return e;
    }
}

/// <summary>
/// The evaluation pass target is not a daily target, and a bot does not tilt.
/// </summary>
public static class TargetTests
{
    public static void Run()
    {
        PassTargetIsSeparate();
        BotsGetNoWall();
    }

    static void PassTargetIsSeparate()
    {
        T.S("pass target vs daily target");

        RuleBook rb = new RuleBook();
        string path = null;
        string[] tries = new string[] { "Ballast/ballast-rules.txt", "ballast-rules.txt" };
        for (int n = 0; n < tries.Length; n++)
            if (System.IO.File.Exists(tries[n])) { path = tries[n]; break; }
        T.Ok(path != null, "the rule book is there");
        if (path == null) return;
        rb.Load(path);

        // A trader's own daily target must survive being configured from the
        // rule book. It used to be overwritten with the firm's pass target -
        // $15,000 on a 250K evaluation - which is not a day, it is a whole
        // evaluation.
        TrackerConfig mine = new TrackerConfig();
        mine.DailyTarget = 750;

        FirmAccountSpec spec = null;
        System.Collections.Generic.List<FirmAccountSpec> all = rb.ForFirm("Apex Trader Funding");
        for (int n = 0; n < all.Count; n++)
            if (all[n].ProfitTarget > 0) { spec = all[n]; break; }
        T.Ok(spec != null, "some account type publishes a profit target");
        if (spec == null) return;

        TrackerConfig c = RuleBook.ToConfig(spec, mine);
        T.Near(c.DailyTarget, 750, 0.01, "the trader's daily target is left alone");
        T.Near(c.ProfitTarget, spec.ProfitTarget, 0.01, "and the firm's pass target is recorded separately");
        T.Ok(c.ProfitTarget != c.DailyTarget, "they are not the same number");

        // Which means protecting a green day works again. With the pass target
        // in the daily slot, peak P&L could never reach it, so ProtectGreen and
        // the give-back warning were both unreachable.
        DisciplineInput i = new DisciplineInput();
        i.CurrentEquity = 251000; i.FloorLevel = 244500; i.CushionToFloor = 6500;
        i.DailyLossLimit = 3000; i.MaxLossesBeforeStop = 3; i.MaxTrades = 5;
        i.MaxContracts = 4; i.NowMinuteEt = 600; i.MinutesSinceLastLoss = -1;
        i.DailyTarget = c.DailyTarget;
        i.DailyPnl = 900; i.PeakDailyPnl = 900;
        T.Eq(DisciplineEngine.Evaluate(i).Action, DisciplineAction.ProtectGreen,
             "a good day is now recognised as a good day");

        i.DailyPnl = 300;   // handed most of it back
        T.Ok(DisciplineEngine.RowWarning(i, DisciplineEngine.Evaluate(i))
                .IndexOf("do not trade back your profits") >= 0,
             "and handing it back is caught");

        // Round trip through the settings file.
        string key;
        TrackerConfig back = SettingsCodec.Deserialise(SettingsCodec.Serialise("Sim110", c), out key);
        T.Near(back.ProfitTarget, spec.ProfitTarget, 0.01, "the pass target is saved and reloaded");
        T.Near(back.DailyTarget, 750, 0.01, "and so is the daily target, still separate");
    }

    static void BotsGetNoWall()
    {
        T.S("bots do not tilt");

        DisciplineInput i = new DisciplineInput();
        i.CurrentEquity = 40000; i.FloorLevel = 47500; i.CushionToFloor = -7500;
        i.DailyLossLimit = 1000; i.DailyPnl = -4000;
        i.MaxLossesBeforeStop = 2; i.LossesToday = 6;
        i.MaxTrades = 5; i.MaxContracts = 2; i.NowMinuteEt = 600; i.MinutesSinceLastLoss = -1;

        DisciplineDecision d = DisciplineEngine.Evaluate(i);

        // By hand, this is about as bad as it gets and the wall belongs there.
        T.Ok(TiltLockout.Evaluate("Sim103", i, d, false).Fired,
             "a hand-traded account in this state gets the wall");

        // The same account run by a strategy. A play account a bot has ground
        // below its floor is not a trader about to revenge trade.
        i.IsAutomated = true;
        DisciplineDecision d2 = DisciplineEngine.Evaluate(i);
        T.Ok(!TiltLockout.Evaluate("Sim103", i, d2, false).Fired,
             "the same state on a strategy account gets no wall");
        T.Eq(TiltLockout.EvaluateAll("Sim103", i, d2, true).Count, 0,
             "not for any reason, including give-back");

        // The risk itself is unchanged - it still says stop, it just does not
        // argue with a person who is not there.
        T.Eq(d2.Action, DisciplineAction.Lockout, "the engine still reports the danger");
        T.Eq(DisciplineEngine.RowWarning(i, d2), "at or below its floor",
             "and the row still says so");
    }
}

/// <summary>
/// Apex evaluation floors, end to end from the shipped rule book.
///
/// This is the number that decides whether an account lives, and getting it
/// wrong in the generous direction is the one failure mode that actually costs
/// money. It has been wrong once already on this project - an evaluation was
/// treated as though its threshold locked, which overstated the room by
/// thousands - so it is pinned here from the rule book file itself rather than
/// from anything hand-typed into a test.
/// </summary>
public static class ApexFloorTests
{
    public static void Run()
    {
        T.S("apex evaluation floors");

        RuleBook rb = new RuleBook();
        string path = null;
        string[] tries = new string[] { "Ballast/ballast-rules.txt", "ballast-rules.txt" };
        for (int n = 0; n < tries.Length; n++)
            if (System.IO.File.Exists(tries[n])) { path = tries[n]; break; }
        T.Ok(path != null, "the rule book ships with the source");
        if (path == null) return;
        T.Ok(rb.Load(path), "and loads");

        List<FirmAccountSpec> apex = rb.ForFirm("Apex Trader Funding");
        T.Ok(apex.Count > 0, "Apex is in the book");

        // Apex publishes THREE behaviours and the rule book must carry all three.
        //   funded                        -> stops at starting + $100
        //   evaluation on Tradovate       -> never stops
        //   evaluation on Rithmic/WealthCharts -> stops at the target profit balance
        int tradovate = 0, rithmic = 0, funded = 0;
        for (int n = 0; n < apex.Count; n++)
        {
            FirmAccountSpec s = apex[n];
            bool isEval = s.Plan.ToLowerInvariant().IndexOf("evaluation") >= 0;
            bool isFunded = s.Plan.ToLowerInvariant().IndexOf("pa") >= 0
                         || s.Plan.ToLowerInvariant().IndexOf("funded") >= 0;
            string platform = RuleBook.PlatformOfPlan(s.Plan);

            // The platform split applies to end-of-day too. This test used to
            // assert the opposite - that no EOD row could lock - which was the
            // conservative reading taken before Apex's EOD article was checked.
            // It says plainly that Rithmic and WealthCharts evaluations "stop
            // trailing and become fixed when the threshold reaches an amount
            // equal to the Target Profit balance", and only Tradovate trails
            // forever. A test that pins a guess stops being a test the moment
            // the guess is checked.
            if (isEval && s.DrawdownType == DrawdownType.EndOfDay)
            {
                if (platform == "TRADOVATE")
                    T.Near(s.LockFloorAt, 0, 0.001,
                           s.Label + " trails forever on Tradovate, end-of-day included");
                else if (platform == "RITHMIC")
                    T.Near(s.LockFloorAt, s.Size + s.ProfitTarget, 0.001,
                           s.Label + " locks at the target profit balance on Rithmic");
            }
            else if (isEval)
            {
                T.Ok(platform.Length > 0,
                     s.Label + " says which platform it is for - the floor depends on it");

                if (platform == "TRADOVATE")
                {
                    tradovate++;
                    T.Near(s.LockFloorAt, 0, 0.001,
                           s.Label + " trails forever on Tradovate");
                }
                else
                {
                    rithmic++;
                    T.Near(s.LockFloorAt, s.Size + s.ProfitTarget, 0.001,
                           s.Label + " fixes at the target profit balance on Rithmic");
                }
            }
            else if (isFunded)
            {
                funded++;
                T.Near(s.LockFloorAt, s.Size + 100, 0.001,
                       s.Label + " locks at starting balance + $100");
            }
        }
        T.Ok(tradovate >= 8, "Tradovate evaluation rows exist, got " + tradovate);
        T.Ok(rithmic >= 8, "Rithmic evaluation rows exist, got " + rithmic);
        T.Ok(funded >= 8, "and funded rows, got " + funded);

        // Connection names map to the right rule set.
        T.Eq(RuleBook.PlatformFromConnection("Rithmic"), "RITHMIC", "Rithmic is recognised");
        T.Eq(RuleBook.PlatformFromConnection("Apex Trader Funding (Rithmic)"), "RITHMIC",
             "and inside a longer connection name");
        T.Eq(RuleBook.PlatformFromConnection("Tradovate"), "TRADOVATE", "so is Tradovate");
        T.Eq(RuleBook.PlatformFromConnection("WealthCharts"), "RITHMIC",
             "WealthCharts follows the Rithmic rule");
        T.Eq(RuleBook.PlatformFromConnection("Interactive Brokers"), "",
             "anything else is unknown rather than guessed");
        T.Eq(RuleBook.PlatformFromConnection(""), "", "and so is nothing");

        // His actual account: legacy 250K evaluation.
        FirmAccountSpec spec = null;
        for (int n = 0; n < apex.Count; n++)
            if (Math.Abs(apex[n].Size - 250000) < 1
                && apex[n].Plan.ToLowerInvariant().IndexOf("evaluation") >= 0
                && RuleBook.PlatformOfPlan(apex[n].Plan) == "RITHMIC") spec = apex[n];

        T.Ok(spec != null, "a 250K legacy evaluation for Rithmic exists");
        if (spec == null) return;

        T.Near(spec.Drawdown, 6500, 0.01, "with a $6,500 drawdown");
        T.Eq(spec.DrawdownType, DrawdownType.Intraday, "trailing intraday");
        T.Near(spec.ProfitTarget, 15000, 0.01, "target $15,000 to pass");
        T.Near(spec.LockFloorAt, 265000, 0.001,
               "threshold fixes at $265,000 - the target profit balance");
        T.Eq(spec.FirmMaxContracts, 27, "and Apex's own 27-contract cap");

        TrackerConfig c = RuleBook.ToConfig(spec, null);

        // At the starting balance: exactly the drawdown of room.
        double cushion = DisciplineEngine.CushionToFloor(
            c.StartingBalance, c.TrailingDrawdown, 250000, 250000, c.DrawdownType, c.LockFloorAt);
        T.Near(cushion, 6500, 0.01, "flat on day one leaves exactly $6,500");
        T.Near(DisciplineEngine.FloorLevel(c.StartingBalance, c.TrailingDrawdown, 250000, 250000,
                                           c.DrawdownType, c.LockFloorAt),
               243500, 0.01, "with the floor at $243,500");

        // His own words: "i could be up at 10k profit and my drawdown is still
        // 6500". The threshold follows the peak all the way up, so profit never
        // becomes cushion on an evaluation.
        cushion = DisciplineEngine.CushionToFloor(250000, 6500, 260000, 260000,
                                                  c.DrawdownType, c.LockFloorAt);
        T.Near(cushion, 6500, 0.01, "up $10,000 and the room is STILL $6,500");
        T.Near(DisciplineEngine.FloorLevel(250000, 6500, 260000, 260000, c.DrawdownType, c.LockFloorAt),
               253500, 0.01, "the floor has followed to $253,500");

        // And it includes unrealised: a winner that round-trips moves the floor
        // up permanently and does not give it back.
        cushion = DisciplineEngine.CushionToFloor(250000, 6500, 254000, 260000,
                                                  c.DrawdownType, c.LockFloorAt);
        T.Near(cushion, 500, 0.01,
               "giving back to $254,000 after a $260,000 peak leaves only $500 - the peak counts");

        // The lock only bites once the PEAK reaches target + drawdown above the
        // start - $271,500 here. Until then it behaves exactly as before, which
        // is why "up $10,000 and still $6,500" is still true.
        T.Ok(!DisciplineEngine.FloorIsLocked(250000, 6500, 260000, 260000, c.DrawdownType, c.LockFloorAt),
             "up $10,000 the threshold has not locked yet");
        T.Ok(DisciplineEngine.FloorIsLocked(250000, 6500, 271500, 271500, c.DrawdownType, c.LockFloorAt),
             "at a $271,500 peak it locks");
        T.Near(DisciplineEngine.CushionToFloor(250000, 6500, 280000, 280000, c.DrawdownType, c.LockFloorAt),
               15000, 0.01,
               "and past the lock, profit becomes real cushion - $15,000 at a $280,000 balance");

        // The Tradovate row on the same account size must NOT lock.
        FirmAccountSpec tv = null;
        for (int n = 0; n < apex.Count; n++)
            if (Math.Abs(apex[n].Size - 250000) < 1
                && RuleBook.PlatformOfPlan(apex[n].Plan) == "TRADOVATE") tv = apex[n];
        T.Ok(tv != null, "the same size exists for Tradovate");
        if (tv != null)
        {
            T.Near(DisciplineEngine.CushionToFloor(250000, 6500, 280000, 280000,
                       DrawdownType.Intraday, tv.LockFloorAt),
                   6500, 0.01,
                   "on Tradovate the same balance still leaves only $6,500 - it never stops trailing");
        }

        // The same size as a FUNDED account does lock, and then profit is real
        // cushion. This is the distinction that was once wrong.
        FirmAccountSpec pa = null;
        for (int n = 0; n < apex.Count; n++)
            if (Math.Abs(apex[n].Size - 250000) < 1
                && apex[n].Plan.ToLowerInvariant().IndexOf("evaluation") < 0) pa = apex[n];

        if (pa != null)
        {
            TrackerConfig pc = RuleBook.ToConfig(pa, null);
            T.Ok(pc.LockFloorAt > 0, "a funded 250K does lock");
            double paCushion = DisciplineEngine.CushionToFloor(250000, pc.TrailingDrawdown,
                260000, 260000, pc.DrawdownType, pc.LockFloorAt);
            T.Ok(paCushion > 6500,
                 "and once locked, profit above the lock IS real cushion - unlike an eval");
        }

        // The safe direction: an unknown lock level must trail, never lock.
        T.Near(DisciplineEngine.FloorLevel(250000, 6500, 300000, 300000, DrawdownType.Intraday, 0),
               293500, 0.01, "a zero lock level trails forever - understating room, never overstating it");
    }
}

/// <summary>
/// The guard against settings that are more generous than the firm's own rules.
/// </summary>
public static class SanityTests
{
    public static void Run()
    {
        T.S("settings that overstate your room");

        RuleBook rb = new RuleBook();
        string path = null;
        string[] tries = new string[] { "Ballast/ballast-rules.txt", "ballast-rules.txt" };
        for (int n = 0; n < tries.Length; n++)
            if (System.IO.File.Exists(tries[n])) { path = tries[n]; break; }
        if (path == null) { T.Ok(false, "rule book present"); return; }
        rb.Load(path);

        // Exactly the state his own settings file was in: a real Apex evaluation
        // configured with a floor that stops trailing.
        TrackerConfig bad = new TrackerConfig();
        bad.StartingBalance = 100000;
        bad.TrailingDrawdown = 6500;
        bad.LockFloorAt = 95000;

        string w = rb.SanityWarning("APEX-11325-109", bad);
        T.Ok(w.Length > 0, "an evaluation with a locking floor is flagged");
        T.Ok(w.IndexOf("evaluation") >= 0, "and told it is an evaluation");
        T.Ok(w.IndexOf("0") >= 0, "with what to set it to");

        // A funded account SHOULD lock - that must not be flagged.
        TrackerConfig pa = new TrackerConfig();
        pa.StartingBalance = 50000;
        pa.TrailingDrawdown = 2000;
        pa.LockFloorAt = 50100;
        T.Eq(rb.SanityWarning("PA-APEX-11325-04", pa), "",
             "a funded account with a locking floor is correct and stays quiet");

        // Too much drawdown for the size.
        TrackerConfig fat = new TrackerConfig();
        fat.StartingBalance = 50000;
        fat.TrailingDrawdown = 6500;
        fat.LockFloorAt = 0;
        string fw = rb.SanityWarning("APEX-11325-109", fat);
        T.Ok(fw.IndexOf("more room than the firm gives you") >= 0,
             "a drawdown bigger than the firm publishes is flagged");

        // Correctly set up: silence.
        TrackerConfig good = new TrackerConfig();
        good.StartingBalance = 250000;
        good.TrailingDrawdown = 6500;
        good.LockFloorAt = 0;
        T.Eq(rb.SanityWarning("APEX-11325-109", good), "",
             "a properly configured legacy 250K evaluation says nothing");

        // Never nag about accounts whose name says nothing about a firm.
        T.Eq(rb.SanityWarning("Sim104", bad), "", "sim accounts are not second-guessed");
        T.Eq(rb.SanityWarning("Playback101", bad), "", "nor playback");
        T.Eq(rb.SanityWarning("", bad), "", "nor a blank name");
        T.Eq(rb.SanityWarning("APEX-11325-109", null), "", "nor a null config");

        // It must only ever fire in the generous direction.
        TrackerConfig tight = new TrackerConfig();
        tight.StartingBalance = 250000;
        tight.TrailingDrawdown = 2000;   // far less room than Apex gives
        tight.LockFloorAt = 0;
        T.Eq(rb.SanityWarning("APEX-11325-109", tight), "",
             "being stricter than the firm is the trader's business, not a fault");
    }
}
