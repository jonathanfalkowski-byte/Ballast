// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastTracker.cs
//
// Turns raw NinjaTrader account events into the flat state the DisciplineEngine
// needs. Deliberately separated from the UI so the counting logic stays testable
// and so a bug here can't take the window down.
//
// Live trades are counted from immutable execution callbacks in a per-instrument
// fill ledger. The older position-delta path remains for saved journals and the
// NinjaTrader-independent regression fixtures.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Ballast
{
    public class TrackerConfig
    {
        public double StartingBalance = 50000;
        public double TrailingDrawdown = 2500;
        public DrawdownType DrawdownType = DrawdownType.Intraday;

        public int MaxLossesBeforeStop = 2;
        public double DailyLossLimit = 500;
        public double DailyTarget = 500;

        /// <summary>
        /// The firm's profit target for PASSING this account - $15,000 on a 250K
        /// Apex evaluation. Nothing to do with a daily target, and it is only
        /// here to be displayed.
        ///
        /// It used to be written straight into DailyTarget by the rule book,
        /// which quietly broke two things at once: "bank it, you have hit your
        /// target" could never fire, and the give-back warning - which only
        /// triggers once the day's peak has passed the daily target - could never
        /// fire either. On any account configured from the rule book, protecting
        /// a green day simply did not work.
        /// </summary>
        public double ProfitTarget = 0;
        public int MaxTrades = 4;
        public int MaxContracts = 1;
        public int CooldownMinutes = 5;

        /// <summary>
        /// Level at which the trailing floor STOPS trailing. Apex locks around
        /// starting + $100; Topstep locks at the original starting balance.
        /// 0 = trails forever.
        /// </summary>
        public double LockFloorAt = 0;

        /// <summary>Which named profile produced these numbers. "" == hand-set.</summary>
        public string ProfileKey = "";

        /// <summary>Percentage of the trailing drawdown intended per trade.</summary>
        public double RiskPctOfDrawdown = 0;

        /// <summary>
        /// Turtle-style size throttle: cut the advised size by ThrottleCutPct for
        /// every ThrottleStepPct of the drawdown already spent. 0 == no throttle.
        /// </summary>
        public double ThrottleStepPct = 0;
        public double ThrottleCutPct = 0;

        /// <summary>Size before the throttle is applied, so it can be restored.</summary>
        public int BaseMaxContracts = 0;

        /// <summary>
        /// This account is traded by a strategy, not by hand.
        ///
        /// It still gets full risk monitoring - arguably more, since nobody is
        /// watching it - but its trades never enter the tag queue and never count
        /// toward the discipline statistics. A bot has no feelings to label and
        /// does not revenge trade; asking "was that planned?" of a strategy is
        /// meaningless, and letting its hundreds of trades into the planned /
        /// unplanned split would drown the handful that measure YOUR behaviour.
        /// </summary>
        public bool IsAutomated = false;

        /// <summary>
        /// What this account is FOR, in the trader's own terms.
        ///
        /// Not the same question as whether the platform calls it a simulator.
        /// He runs a NinjaTrader sim account deliberately as though it were
        /// funded, to test a strategy under something like real conditions - so
        /// the provider says "simulation" and the intent says otherwise. Only he
        /// knows which, and the difference decides what any comparison between
        /// accounts is actually measuring.
        ///
        /// Practice, evaluation and funded are three different psychological
        /// situations. An evaluation has a target and a deadline; funded money
        /// has consequences; practice has neither, which is exactly what makes it
        /// the control in the experiment.
        /// </summary>
        public AccountPurpose Purpose = AccountPurpose.Unsaid;

        /// <summary>The firm's own hard contract cap, 0 if unknown. Never exceeded.</summary>
        public int FirmMaxContracts = 0;

        /// <summary>
        /// The firm's published daily loss limit, 0 when it publishes none.
        ///
        /// Recorded separately from DailyLossLimit for the same reason the
        /// contract cap is: they are two different facts that used to share one
        /// box. Picking an account type wrote the firm's figure straight over the
        /// trader's, so choosing "Apex 250K" - which publishes no daily limit at
        /// all - silently erased "stop me at $500 today" and left the account
        /// running with no daily stop. Now the firm's number is kept here, the
        /// trader's stays where they put it, and the tighter of the two is what
        /// actually binds.
        /// </summary>
        public double FirmDailyLossLimit = 0;

        /// <summary>
        /// Take the day's P&L straight from the account's own realised figure
        /// rather than from the change since Ballast opened.
        ///
        /// Ballast used to measure the day as "realised now, less realised when I
        /// opened". That is exact while Ballast is watching and silently wrong
        /// the moment it is not: a trade taken with the window closed fell
        /// outside the measurement, so the trader was shown a smaller loss than
        /// they had taken and more room than they had left. Saving the baseline
        /// fixes the next restart, but it cannot fix a restart that already
        /// happened, and it cannot fix the very first open of a day.
        ///
        /// A broker that reports realised P&L per session - Rithmic, and every
        /// prop feed built on it - already knows the answer. Taking it directly
        /// means Ballast agrees with the platform's own Accounts tab whether it
        /// saw the trade or not, which is the only number a trader will believe
        /// when the two disagree.
        ///
        /// Turn it off for a feed whose realised figure accumulates across days
        /// rather than resetting each session - NinjaTrader's own Sim accounts
        /// behave that way until they are reset. With it off, Ballast goes back
        /// to measuring from its own baseline.
        /// </summary>
        public bool TrustAccountRealised = true;

        /// <summary>
        /// Which generation THIS account belongs to. Per account, because a trader
        /// can hold legacy and current side by side, and the same balance means a
        /// different drawdown in each.
        /// </summary>
        public AccountGeneration Generation = AccountGeneration.Auto;

        /// <summary>
        /// The trader's own trading window, as minutes past midnight on
        /// NinjaTrader's clock. Start == end means no window, and that is the
        /// default now.
        ///
        /// It used to default to 09:30-11:30 with no setting anywhere to change
        /// it, so every trader who did not happen to trade that exact window was
        /// told "outside your trading window" all day, every day, about a rule
        /// they never chose. A warning that fires when nothing is wrong teaches
        /// people to stop reading the warnings that matter.
        /// </summary>
        public int SessionStartMinute = 0;
        public int SessionEndMinute = 0;

        /// <summary>
        /// Minute at which the prop firm starts a new trading day, measured on
        /// NinjaTrader's configured platform clock. Midnight preserves the old
        /// calendar-day behaviour; firms with an evening boundary can use 18:00.
        /// </summary>
        public int TradingDayResetMinute = 0;
    }

    /// <summary>
    /// Accumulates session state from account callbacks. All mutating methods are
    /// expected to be called from the account event thread; reads are cheap copies.
    /// </summary>
    public class BallastTracker
    {
        public TrackerConfig Config = new TrackerConfig();

        // Session accumulators
        public int TradesToday;
        public int LossesToday;
        public double DailyPnl;          // realised, this session
        public double PeakDailyPnl;      // best realised point this session

        /// <summary>
        /// The worst today has been, and whether it ever reached the daily loss
        /// limit. Both are latched for the session and never walk back up.
        ///
        /// Hitting the limit is an event. Reading it as a state - "am I down more
        /// than my limit right now?" - meant a trader who blew through the limit,
        /// took one more trade and won went from a hard stop back to a caution.
        /// The rule un-fired, as a reward for taking exactly the trade it existed
        /// to prevent.
        /// </summary>
        public double WorstDailyPnl;
        public bool DailyLossLimitHit;
        public DateTime? DailyLossLimitHitAt;
        public DateTime? LastLossAt;
        public bool LastTradeWasLoss;

        public int OpenContracts;
        public double PeakEquity;        // persistent intraday high-water mark; never resets at day rollover
        public double EndOfDayHighWater; // highest completed-session balance for EOD trailing accounts
        public double LastKnownBalance;  // cash/realised balance used to close the previous session
        public double CurrentEquity;

        /// <summary>
        /// Highest firm threshold reported by NinjaTrader's account provider.
        /// AccountItem.TrailingMaxDrawdown is the remaining room, so the actual
        /// threshold is current equity minus that value. Firm thresholds only
        /// move upward; this value is therefore monotonic and persists.
        /// </summary>
        public double AuthoritativeFirmFloor;
        public bool FirmFloorProviderConfirmed;
        public DateTime FirmFloorConfirmedAt = DateTime.MinValue;

        /// <summary>
        /// False until we've seen a believable equity reading. A disconnected or
        /// not-yet-loaded account reports 0, and computing a cushion from that
        /// produces an alarming nonsense number - so we report "no data" instead.
        /// </summary>
        public bool HasValidEquity;

        /// <summary>
        /// False after the account's actual positions disagree with the execution
        /// ledger. Ballast then blocks advice instead of inventing a trade.
        /// </summary>
        /// <summary>
        /// True when this account looks like it was reset out from under Ballast.
        ///
        /// Resetting a simulation account - or a firm resetting a failed
        /// evaluation - puts the balance back and zeroes the platform's own P&L,
        /// but tells Ballast nothing. So the window carries on reporting a day
        /// that no longer exists: "was up 2,726, handed back 2,726" against an
        /// account whose own records show neither number.
        ///
        /// Nothing is cleared on the strength of this. It raises an offer on the
        /// row and waits to be told, because the same accumulators hold the
        /// latched daily loss limit on live accounts, and a bad feed reading must
        /// never be able to un-spend a day.
        /// </summary>
        public bool ResetSuspected;

        /// <summary>
        /// When the trader last said "this account was reset, start it over".
        /// Journal rows that closed before this belong to the erased day and are
        /// not counted back in on restart.
        /// </summary>
        public DateTime RestartedAt = DateTime.MinValue;

        /// <summary>
        /// What the account opened the day with: cash MINUS the day's realised
        /// P&L. Trading moves both together, so this figure should not move at
        /// all between the first tick of the session and the last.
        ///
        /// When it does move, the account's base has been changed underneath
        /// Ballast - a simulation account re-sized, a firm re-issuing an
        /// evaluation. Unlike a balance jump, this survives being written down,
        /// so it catches a reset done while Ballast was CLOSED, which is when
        /// most of them happen.
        /// </summary>
        public double DayOpenBalance;

        /// <summary>
        /// Whether a fill arrived since the last equity update. Trading cannot
        /// move the balance of a flat account, so money that moves with no fill
        /// behind it did not come from the market.
        /// </summary>
        private bool fillSinceEquity;

        /// <summary>
        /// A jump seen once and not yet believed. A fill's cash update and its
        /// position update do not always arrive in that order, so a reading taken
        /// between the two looks exactly like money appearing from nowhere.
        /// Waiting one more reading lets the position update land.
        /// </summary>
        private bool resetPending;

        public bool ExecutionTelemetryHealthy = true;
        public string ExecutionTelemetryWarning = "";

        private double sessionStartRealised;
        private bool haveBaseline;
        private bool inPosition;
        private double realisedAtTradeOpen;
        private DateTime sessionDate = DateTime.MinValue.Date;

        // ── Today's count, recovered from the journal ────────────────────────
        //
        // A tracker is built fresh every time the Ballast window opens, so its
        // counters start at zero. Close the window at lunchtime and reopen it and
        // the account has apparently taken no trades and no losses today - which
        // means the max-trades rule, the loss-streak stop and the lockout can all
        // be cleared by closing a window. That is not a rule; that is a rule with
        // an off switch nobody documented.
        //
        // The journal already knows what happened today, so the counts are seeded
        // from it. Held as a pending seed rather than written directly because
        // EnsureSession zeroes the counters the first time it runs for a new
        // session date, and would otherwise wipe them straight back out.
        private bool seedPending;
        private DateTime seedDay = DateTime.MinValue.Date;
        private int seedTrades;
        private int seedLosses;
        private DateTime? seedLastLossAt;
        private bool seedLastWasLoss;
        private double seedDailyPnl;
        private double seedWorstDailyPnl;

        /// <summary>
        /// The baseline is wound back by the journal's total ONCE per session.
        /// Re-seeding is otherwise safe and happens whenever the journal gains a
        /// row that Ballast did not watch - but winding the baseline a second
        /// time would move the whole day by the morning's P&L again.
        /// </summary>
        private bool seedBaselineApplied;

        // ── The session baseline, recovered from disk ────────────────────────
        //
        // The journal seed can only put back what Ballast SAW. A trade opened and
        // closed while the window was shut leaves no journal row, so the day's
        // P&L came back short by exactly that trade - and "left to lose" was
        // therefore too generous, which is the dangerous direction to be wrong in.
        //
        // The fix does not need to know what the trade was. Ballast measures the
        // day as the change in the account's realised P&L since a baseline, so if
        // the baseline itself survives the restart, everything that happened in
        // between is inside the measurement whether Ballast watched it or not.
        private bool sessionSeedPending;
        private bool sessionRestored;
        private DateTime sessionSeedDay = DateTime.MinValue.Date;
        private double sessionSeedStartRealised;
        private double sessionSeedPeakEquity;
        private double sessionSeedPeakDailyPnl;
        private double sessionSeedWorstDailyPnl;
        private bool sessionSeedLimitHit;

        // Drawdown state is account-lifetime state, not daily state. It is kept
        // separately from the session baseline so yesterday's P&L counters can
        // reset without quietly lowering a trailing floor.
        private DateTime riskStateAsOf = DateTime.MinValue.Date;

        /// <summary>The realised P&L this session was measured from.</summary>
        public double SessionStartRealised { get { return sessionStartRealised; } }

        /// <summary>The trading day this tracker is currently on.</summary>
        public DateTime SessionDate { get { return sessionDate; } }

        /// <summary>Map a wall-clock instant onto the firm's trading-day key.</summary>
        public DateTime TradingDay(DateTime now)
        {
            int reset = Config == null ? 0 : Config.TradingDayResetMinute;
            if (reset < 0 || reset >= 1440) reset = 0;
            int minute = now.Hour * 60 + now.Minute;
            return reset > 0 && minute < reset ? now.Date.AddDays(-1) : now.Date;
        }

        /// <summary>True when this session's baseline came back from disk rather than from today's open.</summary>
        public bool SessionRestored { get { return sessionRestored; } }

        /// <summary>
        /// True when the day's P&L can be trusted to include trading Ballast did
        /// not watch - either because the baseline came back from disk, or
        /// because the figure is the account's own. Only then is a difference
        /// between the day's P&L and the journal evidence of a missing trade
        /// rather than evidence of a missing baseline.
        /// </summary>
        public bool BaselineAuthoritative { get { return sessionRestored; } }

        /// <summary>
        /// Hand back the exact baseline this session was being measured from
        /// before Ballast closed, so anything traded while it was shut is still
        /// inside the day's figure.
        ///
        /// The peak equity comes back too: an intraday trailing floor ratchets up
        /// with equity and never comes down, so forgetting a peak makes the
        /// cushion look bigger than it is.
        /// </summary>
        public void SeedSession(DateTime day, double startRealised, double peakEquity,
                                double peakDailyPnl, double worstDailyPnl, bool limitHit)
        {
            // Version-1 session files only carried one peak. Treat it as both
            // anchors during migration. That can be conservative for an EOD
            // account, but it can never invent extra room.
            SeedSession(day, startRealised, peakEquity, peakDailyPnl,
                        worstDailyPnl, limitHit, peakEquity, peakEquity);
        }

        /// <summary>
        /// Restore daily session state plus account-lifetime drawdown anchors.
        /// The latter are applied even when the saved day is yesterday.
        /// </summary>
        public void SeedSession(DateTime day, double startRealised, double peakEquity,
                                double peakDailyPnl, double worstDailyPnl, bool limitHit,
                                double endOfDayHighWater, double lastKnownBalance)
        {
            SeedSession(day, startRealised, peakEquity, peakDailyPnl, worstDailyPnl,
                        limitHit, endOfDayHighWater, lastKnownBalance, 0, false);
        }

        public void SeedSession(DateTime day, double startRealised, double peakEquity,
                                double peakDailyPnl, double worstDailyPnl, bool limitHit,
                                double endOfDayHighWater, double lastKnownBalance,
                                double authoritativeFirmFloor, bool providerConfirmed)
        {
            SeedRiskState(day, peakEquity, endOfDayHighWater, lastKnownBalance,
                          authoritativeFirmFloor, providerConfirmed);

            sessionSeedPending = true;
            sessionSeedDay = day.Date;
            sessionSeedStartRealised = startRealised;
            sessionSeedPeakEquity = peakEquity;
            sessionSeedPeakDailyPnl = peakDailyPnl;
            sessionSeedWorstDailyPnl = worstDailyPnl;
            sessionSeedLimitHit = limitHit;

            if (sessionDate == sessionSeedDay) ApplySessionSeed();
        }

        /// <summary>
        /// Restore the risk anchors without restoring yesterday's daily counters
        /// or realised-P&amp;L baseline.
        /// </summary>
        public void SeedRiskState(DateTime asOfDay, double peakEquity,
                                  double endOfDayHighWater, double lastKnownBalance)
        {
            SeedRiskState(asOfDay, peakEquity, endOfDayHighWater, lastKnownBalance, 0, false);
        }

        public void SeedRiskState(DateTime asOfDay, double peakEquity,
                                  double endOfDayHighWater, double lastKnownBalance,
                                  double authoritativeFirmFloor, bool providerConfirmed)
        {
            if (riskStateAsOf != DateTime.MinValue.Date && asOfDay.Date < riskStateAsOf) return;

            if (IsPlausibleEquity(peakEquity) && peakEquity > PeakEquity)
                PeakEquity = peakEquity;
            if (IsPlausibleEquity(endOfDayHighWater) && endOfDayHighWater > EndOfDayHighWater)
                EndOfDayHighWater = endOfDayHighWater;
            if (IsPlausibleEquity(lastKnownBalance)) LastKnownBalance = lastKnownBalance;
            if (IsPlausibleEquity(authoritativeFirmFloor)
                && authoritativeFirmFloor > AuthoritativeFirmFloor)
                AuthoritativeFirmFloor = authoritativeFirmFloor;
            if (providerConfirmed && AuthoritativeFirmFloor > 0)
                FirmFloorProviderConfirmed = true;

            riskStateAsOf = asOfDay.Date;
        }

        /// <summary>
        /// Accept the provider's remaining trailing-drawdown room and turn it
        /// into an authoritative threshold. Values outside the active firm's
        /// published bounds are rejected rather than allowed to change cushion.
        /// </summary>
        public bool ObserveProviderTrailingRoom(double remainingRoom,
                                                double currentEquity, DateTime when)
        {
            if (Config == null || !IsPlausibleEquity(currentEquity)) return false;
            if (double.IsNaN(remainingRoom) || double.IsInfinity(remainingRoom)
                || remainingRoom <= 0 || remainingRoom > currentEquity) return false;

            double candidate = currentEquity - remainingRoom;
            double initialFloor = Config.StartingBalance - Config.TrailingDrawdown;
            const double tolerance = 5.0;

            if (candidate < initialFloor - tolerance) return false;
            if (Config.LockFloorAt > 0 && candidate > Config.LockFloorAt + tolerance) return false;

            if (candidate < initialFloor) candidate = initialFloor;
            if (Config.LockFloorAt > 0 && candidate > Config.LockFloorAt)
                candidate = Config.LockFloorAt;

            if (!IsPlausibleEquity(candidate)) return false;
            if (candidate > AuthoritativeFirmFloor) AuthoritativeFirmFloor = candidate;
            FirmFloorProviderConfirmed = true;
            FirmFloorConfirmedAt = when;
            return true;
        }

        private double ValidAuthoritativeFirmFloor()
        {
            if (!IsPlausibleEquity(AuthoritativeFirmFloor) || Config == null) return 0;
            double initialFloor = Config.StartingBalance - Config.TrailingDrawdown;
            if (AuthoritativeFirmFloor < initialFloor - 5) return 0;
            if (Config.LockFloorAt > 0 && AuthoritativeFirmFloor > Config.LockFloorAt + 5) return 0;
            return Config.LockFloorAt > 0 && AuthoritativeFirmFloor > Config.LockFloorAt
                ? Config.LockFloorAt : AuthoritativeFirmFloor;
        }

        private void ApplySessionSeed()
        {
            if (!sessionSeedPending) return;
            if (sessionDate != sessionSeedDay) return;

            // The baseline on disk is Ballast's OWN measuring point, and it only
            // applies when Ballast is doing the measuring. With the account's own
            // realised figure in use the day starts at zero and the platform
            // supplies the rest - restoring a saved baseline on top of that
            // subtracts this morning twice and hands back exactly the too-small
            // loss the whole change exists to stop.
            //
            // This bit hard on the very first run of the new build: the file on
            // disk had been written minutes earlier by the old one, so the
            // account said the day had cost $2,126 and Ballast still said $754.
            if (Config == null || !Config.TrustAccountRealised)
                sessionStartRealised = sessionSeedStartRealised;

            haveBaseline = true;
            sessionRestored = true;

            if (sessionSeedPeakDailyPnl > PeakDailyPnl) PeakDailyPnl = sessionSeedPeakDailyPnl;
            if (sessionSeedWorstDailyPnl < WorstDailyPnl) WorstDailyPnl = sessionSeedWorstDailyPnl;
            if (sessionSeedLimitHit) DailyLossLimitHit = true;

            // Only ever upward. A peak that has been reached cannot be unreached,
            // and the floor that follows it does not come back down.
            if (IsPlausibleEquity(sessionSeedPeakEquity) && sessionSeedPeakEquity > PeakEquity)
                PeakEquity = sessionSeedPeakEquity;

            sessionSeedPending = false;
        }

        /// <summary>
        /// Tell this tracker what the journal says has already happened today.
        /// Applied when the session for that date opens, and ignored once the
        /// date has moved on.
        /// </summary>
        public void SeedToday(DateTime day, int trades, int losses,
                              DateTime? lastLossAt, bool lastWasLoss, double dailyPnl)
        {
            SeedToday(day, trades, losses, lastLossAt, lastWasLoss, dailyPnl, dailyPnl);
        }

        /// <summary>
        /// As above, but also restoring the worst the day has been. The trough
        /// matters on its own: a trader who was down past their limit at 10:30
        /// and has since won some of it back has still spent the day, and closing
        /// the window must not be a way to un-spend it.
        /// </summary>
        public void SeedToday(DateTime day, int trades, int losses,
                              DateTime? lastLossAt, bool lastWasLoss, double dailyPnl,
                              double worstDailyPnl)
        {
            seedWorstDailyPnl = worstDailyPnl < dailyPnl ? worstDailyPnl : dailyPnl;
            seedPending = true;
            seedDay = day.Date;
            seedTrades = trades < 0 ? 0 : trades;
            seedLosses = losses < 0 ? 0 : losses;
            seedLastLossAt = lastLossAt;
            seedLastWasLoss = lastWasLoss;
            seedDailyPnl = dailyPnl;

            // If this tracker has already opened today's session - the window has
            // been running a while and the journal loaded late - apply it now.
            if (sessionDate == seedDay) ApplySeed();
        }

        private void ApplySeed()
        {
            if (!seedPending) return;
            if (sessionDate != seedDay) return;

            TradesToday = seedTrades;
            LossesToday = seedLosses;
            LastLossAt = seedLastLossAt;
            LastTradeWasLoss = seedLastWasLoss;

            // The day's P&L is worked out as the change in realised P&L since the
            // session's baseline. Reopening the window set that baseline to
            // wherever the account happened to be, so the morning's profit or
            // loss vanished and the day appeared to start again at zero. Winding
            // the baseline back by what the journal says has already been made
            // puts the real figure back.
            // Winding the baseline back by what the journal says has already been
            // made is only right when the baseline was set at THIS open. If the
            // real baseline came back from disk it is already exact, and it
            // covers trades the journal never saw - subtracting the journal's
            // total from it as well would count the morning twice.
            if (haveBaseline && !sessionRestored && !seedBaselineApplied)
            {
                seedBaselineApplied = true;
                sessionStartRealised -= seedDailyPnl;
                DailyPnl = seedDailyPnl;
                if (DailyPnl > PeakDailyPnl) PeakDailyPnl = DailyPnl;
            }

            // The worst point of the morning, from the journal. Without it,
            // closing and reopening the window would clear a daily loss limit
            // that had already been hit - the same "a rule with an off switch
            // nobody documented" problem the trade count had.
            if (seedWorstDailyPnl < WorstDailyPnl) WorstDailyPnl = seedWorstDailyPnl;
            NoteTrough(DateTime.MinValue);

            // Once only. A second application would double the count the first
            // time a new trade arrived.
            seedPending = false;
        }

        // Snapshot of the world at the moment the current trade was opened.
        private string openInstrument = "";
        private bool openIsLong;
        private int openMaxContracts;
        private DateTime openedAt;
        private double openDailyPnl;
        private double openCushion;
        private double openFloor;
        private int openMinsSinceLoss = -1;
        private bool openPrevWasLoss;
        private bool openInWindow;
        private double openCommission;
        private string openAdvice = "";
        private string openImage = "";

        // Exact, per-instrument execution state. This is separate from the
        // legacy position-polling fields above so old journals and tests keep
        // loading while the live add-on uses immutable fills.
        private sealed class ExecutionTradeState
        {
            public int Quantity;
            public double AveragePrice;
            public double PointValue = 1;
            public double GrossPnl;
            public double Commission;
            public bool IsLong;
            public int MaxContracts;
            public DateTime OpenedAt;
            public double DailyPnlBefore;
            public double CushionAtEntry;
            public double FloorAtEntry;
            public int MinutesSincePreviousLoss;
            public bool PreviousTradeWasLoss;
            public bool InsideSessionWindow;
            public string AdviceAtEntry = "";
            public string EntryImage = "";
            public string Note = "";
        }

        private readonly Dictionary<string, ExecutionTradeState> executionTrades =
            new Dictionary<string, ExecutionTradeState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> seenExecutionIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> executionIdOrder = new Queue<string>();
        private const int MaxRememberedExecutionIds = 4096;

        public List<string> ExecutionInstruments
        {
            get { return new List<string>(executionTrades.Keys); }
        }

        public int ExecutionPosition(string instrument)
        {
            ExecutionTradeState s;
            return instrument != null && executionTrades.TryGetValue(instrument, out s)
                ? s.Quantity : 0;
        }

        private void RememberExecution(string executionId)
        {
            if (string.IsNullOrEmpty(executionId)) return;
            seenExecutionIds.Add(executionId);
            executionIdOrder.Enqueue(executionId);
            while (executionIdOrder.Count > MaxRememberedExecutionIds)
                seenExecutionIds.Remove(executionIdOrder.Dequeue());
        }

        private void RecountExecutionContracts()
        {
            int total = 0;
            foreach (ExecutionTradeState s in executionTrades.Values)
                total += Math.Abs(s.Quantity);
            OpenContracts = total;
        }

        private ExecutionTradeState NewExecutionTrade(string instrument, int quantity,
                                                       double price, double pointValue,
                                                       DateTime now, string note)
        {
            ExecutionTradeState s = new ExecutionTradeState();
            s.Quantity = quantity;
            s.AveragePrice = price;
            s.PointValue = pointValue > 0 ? pointValue : 1;
            s.IsLong = quantity > 0;
            s.MaxContracts = Math.Abs(quantity);
            s.OpenedAt = now;
            s.DailyPnlBefore = DailyPnl;
            s.MinutesSincePreviousLoss = MinutesSinceLastLoss(now);
            s.PreviousTradeWasLoss = LastTradeWasLoss;
            s.Note = note ?? "";

            DisciplineInput snap = BuildInput(now);
            s.CushionAtEntry = HasValidEquity ? snap.CushionToFloor : 0;
            s.FloorAtEntry = snap.FloorLevel;
            s.InsideSessionWindow = DisciplineEngine.InSessionWindow(
                snap.NowMinuteEt, Config.SessionStartMinute, Config.SessionEndMinute);
            s.AdviceAtEntry = DisciplineEngine.Evaluate(snap).Action.ToString();

            if (CaptureChart != null)
            {
                try { s.EntryImage = CaptureChart(instrument, now, true) ?? ""; }
                catch { s.EntryImage = ""; }
            }

            return s;
        }

        /// <summary>
        /// Seed a position that was already open when Ballast attached. Entry
        /// time/context are explicitly marked approximate rather than invented.
        /// </summary>
        public void SeedOpenInstrument(string instrument, int signedQuantity,
                                       double averagePrice, double pointValue, DateTime now)
        {
            if (string.IsNullOrEmpty(instrument) || signedQuantity == 0) return;
            if (executionTrades.ContainsKey(instrument)) return;

            executionTrades[instrument] = NewExecutionTrade(
                instrument, signedQuantity, averagePrice, pointValue, now,
                "Ballast began watching after this position was already open; entry time and entry context are approximate.");
            RecountExecutionContracts();
        }

        public void MarkExecutionTelemetryGap(string warning)
        {
            ExecutionTelemetryHealthy = false;
            ExecutionTelemetryWarning = string.IsNullOrEmpty(warning)
                ? "Position state does not match the execution ledger." : warning;
        }

        public void ResetExecutionTelemetry()
        {
            ExecutionTelemetryHealthy = true;
            ExecutionTelemetryWarning = "";
        }

        private BallastTrade FinishExecutionTrade(string instrument, string accountName,
                                                  ExecutionTradeState s, DateTime now)
        {
            TradesToday++;
            if (s.GrossPnl < 0)
            {
                LossesToday++;
                LastLossAt = now;
                LastTradeWasLoss = true;
            }
            else LastTradeWasLoss = false;

            BallastTrade e = new BallastTrade();
            e.AccountName = accountName ?? "";
            e.Instrument = instrument ?? "";
            e.IsLong = s.IsLong;
            e.MaxContracts = s.MaxContracts;
            e.EntryTime = s.OpenedAt;
            e.ExitTime = now;
            e.Pnl = s.GrossPnl;
            e.Commission = s.Commission > 0 ? s.Commission : 0;
            e.TradeNumberToday = TradesToday;
            e.DailyPnlBefore = s.DailyPnlBefore;
            e.CushionAtEntry = s.CushionAtEntry;
            e.FloorAtEntry = s.FloorAtEntry;
            e.MinutesSincePreviousLoss = s.MinutesSincePreviousLoss;
            e.PreviousTradeWasLoss = s.PreviousTradeWasLoss;
            e.InsideSessionWindow = s.InsideSessionWindow;
            e.AdviceAtEntry = s.AdviceAtEntry;
            e.Automated = Config.IsAutomated;
            e.EntryImage = s.EntryImage;
            e.Note = s.Note;

            if (CaptureChart != null)
            {
                try { e.ExitImage = CaptureChart(instrument, now, false) ?? ""; }
                catch { e.ExitImage = ""; }
            }

            return e;
        }

        /// <summary>
        /// Apply one immutable fill. signedQuantity is positive for buys and
        /// buy-to-cover, negative for sells and sell-short. Stable duplicate IDs
        /// are ignored and reversals close then reopen at the same fill.
        /// </summary>
        public BallastTrade OnExecution(string executionId, string instrument,
                                        int signedQuantity, double price,
                                        double pointValue, double commission,
                                        DateTime now, string accountName)
        {
            if (signedQuantity == 0 || string.IsNullOrEmpty(instrument)) return null;
            if (!string.IsNullOrEmpty(executionId) && seenExecutionIds.Contains(executionId)) return null;
            RememberExecution(executionId);

            ExecutionTradeState s;
            if (!executionTrades.TryGetValue(instrument, out s))
            {
                s = NewExecutionTrade(instrument, signedQuantity, price, pointValue, now, "");
                s.Commission = commission > 0 ? commission : 0;
                executionTrades[instrument] = s;
                RecountExecutionContracts();
                return null;
            }

            int oldQuantity = s.Quantity;
            int newQuantity = oldQuantity + signedQuantity;
            double fillCommission = commission > 0 ? commission : 0;

            if ((oldQuantity > 0 && signedQuantity > 0) || (oldQuantity < 0 && signedQuantity < 0))
            {
                s.Commission += fillCommission;
                int oldAbs = Math.Abs(oldQuantity), addAbs = Math.Abs(signedQuantity);
                s.AveragePrice = ((s.AveragePrice * oldAbs) + (price * addAbs)) / (oldAbs + addAbs);
                s.Quantity = newQuantity;
                if (Math.Abs(newQuantity) > s.MaxContracts) s.MaxContracts = Math.Abs(newQuantity);
                RecountExecutionContracts();
                return null;
            }

            int closing = Math.Min(Math.Abs(oldQuantity), Math.Abs(signedQuantity));
            double closingCommission = fillCommission * closing / Math.Abs(signedQuantity);
            s.Commission += closingCommission;
            double multiplier = s.PointValue > 0 ? s.PointValue : (pointValue > 0 ? pointValue : 1);
            s.GrossPnl += oldQuantity > 0
                ? (price - s.AveragePrice) * multiplier * closing
                : (s.AveragePrice - price) * multiplier * closing;

            if (newQuantity != 0 && ((newQuantity > 0) == (oldQuantity > 0)))
            {
                s.Quantity = newQuantity;
                RecountExecutionContracts();
                return null;
            }

            BallastTrade closed = FinishExecutionTrade(instrument, accountName, s, now);
            executionTrades.Remove(instrument);

            if (newQuantity != 0)
            {
                ExecutionTradeState reversed = NewExecutionTrade(
                    instrument, newQuantity, price, pointValue, now,
                    "Position reversed in a single execution.");
                reversed.Commission = fillCommission - closingCommission;
                executionTrades[instrument] = reversed;
            }

            RecountExecutionContracts();
            return closed;
        }

        /// <summary>
        /// Set by the host so the tracker can photograph the chart without knowing
        /// anything about NinjaTrader or WPF. Null in tests, which is the point:
        /// the counting logic stays testable and a capture bug cannot reach it.
        /// </summary>
        public Func<string, DateTime, bool, string> CaptureChart;

        /// <summary>
        /// The account's running commission total, set by the host before each
        /// position update so a completed round trip can record what it cost.
        ///
        /// Set rather than passed because it is a courtesy figure, not part of
        /// the counting logic: if the host never sets it, every trade records 0
        /// commission and nothing else behaves differently.
        /// </summary>
        public double CurrentCommission;

        /// <summary>
        /// Record the day's low-water mark and latch the daily loss limit the
        /// first time it is reached. Called wherever DailyPnl moves.
        /// </summary>
        private void NoteTrough(DateTime when)
        {
            if (DailyPnl < WorstDailyPnl) WorstDailyPnl = DailyPnl;

            if (!DailyLossLimitHit && Config != null && Config.DailyLossLimit > 0
                && WorstDailyPnl <= -Math.Abs(Config.DailyLossLimit))
            {
                DailyLossLimitHit = true;
                if (when != DateTime.MinValue) DailyLossLimitHitAt = when;
            }
        }

        /// <summary>Reset accumulators when a new trading day starts.</summary>
        public void EnsureSession(DateTime now, double realisedNow, double equityNow)
        {
            DateTime tradingDay = TradingDay(now);
            if (sessionDate != tradingDay)
            {
                // An EOD threshold advances from the completed session's closing
                // balance, not from the account's fluctuating intraday balance.
                // Do this before replacing any of yesterday's state.
                if (sessionDate != DateTime.MinValue.Date && IsPlausibleEquity(LastKnownBalance)
                    && LastKnownBalance > EndOfDayHighWater)
                    EndOfDayHighWater = LastKnownBalance;

                sessionDate = tradingDay;
                DayOpenBalance = 0;          // relearned from the new day's first reading
                resetPending = false;
                ResetSuspected = false;
                TradesToday = 0;
                LossesToday = 0;
                DailyPnl = 0;
                PeakDailyPnl = 0;
                WorstDailyPnl = 0;
                DailyLossLimitHit = false;
                DailyLossLimitHitAt = null;
                LastLossAt = null;
                LastTradeWasLoss = false;
                // The account's own figure for the day, when the feed reports one.
                // See TrackerConfig.TrustAccountRealised - this is what makes
                // Ballast agree with the platform's Accounts tab about a trade it
                // never saw.
                bool trust = Config != null && Config.TrustAccountRealised;
                sessionStartRealised = trust ? 0 : realisedNow;
                haveBaseline = true;
                sessionRestored = trust;
                seedBaselineApplied = false;
                inPosition = false;
                bool ok = IsPlausibleEquity(equityNow);
                if (ok && (!IsPlausibleEquity(PeakEquity) || equityNow > PeakEquity))
                    PeakEquity = equityNow;
                if (!IsPlausibleEquity(EndOfDayHighWater) && ok)
                    EndOfDayHighWater = Math.Max(Config.StartingBalance, equityNow);
                CurrentEquity = ok ? equityNow : 0;
                HasValidEquity = ok;

                // The baseline Ballast was measuring this day from before it was
                // closed, if it was open earlier today. This comes FIRST, because
                // the journal seed below behaves differently when the baseline is
                // already exact.
                ApplySessionSeed();

                // Put back what the journal says already happened today. Only
                // ever for today - yesterday's trades must not follow the trader
                // into a new session.
                ApplySeed();
            }
            else if (!haveBaseline)
            {
                sessionStartRealised = Config != null && Config.TrustAccountRealised ? 0 : realisedNow;
                haveBaseline = true;
            }
        }

        /// <summary>
        /// Is this equity reading believable for the account we're configured for?
        ///
        /// A disconnected or still-loading account reports 0. Occasionally a feed
        /// reports something wildly off. Either poisons the peak permanently, and
        /// since the floor never comes back down, one bad tick produces a wrong
        /// cushion for the rest of the session. So readings are sanity-checked
        /// against the configured account size before they're trusted at all.
        /// </summary>
        public bool IsPlausibleEquity(double equity)
        {
            if (equity <= 0) return false;
            if (Config.StartingBalance <= 0) return true;   // nothing to compare against

            // A prop account lives in a narrow band: it dies a drawdown below its
            // start, and traders withdraw rather than let it balloon. Anything
            // outside 0.5x - 1.5x of the configured size is a bad reading, not a
            // real balance. (This is what produced the -$97,500 cushion: a single
            // spurious high tick permanently poisoned the peak.)
            return equity >= Config.StartingBalance * 0.5
                && equity <= Config.StartingBalance * 1.5;
        }

        /// <summary>Call whenever account equity/balance changes.</summary>
        public void OnEquity(double equityNow, double realisedNow)
        {
            OnEquity(equityNow, realisedNow, equityNow);
        }

        /// <summary>
        /// Update live equity while retaining the cash/realised balance needed to
        /// advance an end-of-day threshold at the next session boundary.
        /// </summary>
        public void OnEquity(double equityNow, double realisedNow, double balanceNow)
        {
            if (!IsPlausibleEquity(equityNow))
            {
                HasValidEquity = false;
                fillSinceEquity = false;
                return;
            }

            if (!ResetSuspected)
            {
                if (resetPending)
                {
                    // A fill turned up in the meantime, so the market explains it.
                    if (!fillSinceEquity && OpenContracts == 0) ResetSuspected = true;
                    resetPending = false;
                }
                else if (LooksReset(equityNow, realisedNow, balanceNow))
                {
                    resetPending = true;
                }
            }
            fillSinceEquity = false;

            HasValidEquity = true;
            CurrentEquity = equityNow;
            if (IsPlausibleEquity(balanceNow)) LastKnownBalance = balanceNow;

            // Heal a peak that was poisoned before we could judge it.
            if (!IsPlausibleEquity(PeakEquity)) PeakEquity = equityNow;

            if (equityNow > PeakEquity) PeakEquity = equityNow;

            if (haveBaseline)
            {
                DailyPnl = realisedNow - sessionStartRealised;
                if (DailyPnl > PeakDailyPnl) PeakDailyPnl = DailyPnl;
                NoteTrough(DateTime.MinValue);
            }

            // Learn the day's base from the first believable reading of it.
            if (DayOpenBalance <= 0 && IsPlausibleEquity(balanceNow))
                DayOpenBalance = balanceNow - realisedNow;
        }

        /// <summary>
        /// Did this account just get put back to the start?
        ///
        /// Four things have to be true together, and each one rules out a way of
        /// being wrong:
        ///
        ///   - the platform's own realised figure is exactly zero. A reset zeroes
        ///     it. Ordinary trading lands on exactly 0.00 only by coincidence.
        ///   - the account is flat, so there is no open trade whose settlement
        ///     could explain the move.
        ///   - no fill has arrived since the last reading. This is the one that
        ///     matters: a trader who was down 2,000 and won it back to exactly
        ///     zero got there THROUGH a closing fill, and must not have his day
        ///     un-spent by this. Money that moves with no fill behind it did not
        ///     come from the market.
        ///   - the balance actually jumped, by more than commission or a
        ///     settlement adjustment could account for.
        ///
        /// And there has to be something to erase. An account that has done
        /// nothing today is not "reset", it is just quiet.
        /// </summary>
        private bool LooksReset(double equityNow, double realisedNow, double balanceNow)
        {
            if (!haveBaseline) return false;
            if (OpenContracts != 0) return false;
            if (fillSinceEquity) return false;

            // Commission does not always land in the same instant as the fill it
            // belongs to, so a few dollars of drift between cash and realised is
            // ordinary. A reset moves thousands.
            double noise = Math.Max(500.0, CurrentCommission * 3.0);

            // The base moved. This is the one that survives a restart: the figure
            // is written into the session file, so an account re-sized while
            // Ballast was closed is caught the moment it reopens.
            if (DayOpenBalance > 0 && IsPlausibleEquity(balanceNow)
                && Math.Abs((balanceNow - realisedNow) - DayOpenBalance) > noise)
                return true;

            // The balance moved and no fill did it. This is what catches a plain
            // reset back to the same starting figure, where the base never
            // changes and only the day's P&L is wiped.
            //
            // And there has to be something to erase: an account that has done
            // nothing today is not "reset", it is just quiet.
            if (HasValidEquity && CurrentEquity > 0
                && Math.Abs(equityNow - CurrentEquity) > noise
                && (TradesToday > 0 || DailyLossLimitHit
                    || PeakDailyPnl != 0 || WorstDailyPnl != 0 || DailyPnl != 0))
                return true;

            return false;
        }

        /// <summary>
        /// Take the account's current base as correct from here on. Used when the
        /// trader says a suspected reset was nothing, so the same question is not
        /// asked again every second.
        /// </summary>
        public void AnchorDayOpen(double realisedNow, double balanceNow)
        {
            if (IsPlausibleEquity(balanceNow)) DayOpenBalance = balanceNow - realisedNow;
            resetPending = false;
        }

        /// <summary>Restore the day's opening base read back from the session file.</summary>
        public void SeedDayOpen(double dayOpen)
        {
            if (dayOpen > 0) DayOpenBalance = dayOpen;
        }

        /// <summary>
        /// Start this account's day over, because the account itself was.
        ///
        /// Everything the day was measured from is gone: the peak equity that the
        /// trailing floor hangs off, the trade and loss counts, the latched daily
        /// limit. Leaving the peak behind would be the expensive one - the floor
        /// would stay anchored to a balance the account no longer has and report a
        /// cushion that does not exist.
        ///
        /// This is the one thing in Ballast that un-spends a day, which is why it
        /// only ever happens when the trader says so.
        /// </summary>
        public void StartOver(DateTime now, double realisedNow, double equityNow)
        {
            TradesToday = 0;
            LossesToday = 0;
            DailyPnl = 0;
            PeakDailyPnl = 0;
            WorstDailyPnl = 0;
            DailyLossLimitHit = false;
            DailyLossLimitHitAt = null;
            LastLossAt = null;
            LastTradeWasLoss = false;
            inPosition = false;

            sessionStartRealised = Config != null && Config.TrustAccountRealised ? 0 : realisedNow;
            haveBaseline = true;
            sessionRestored = false;
            seedBaselineApplied = true;      // the seed describes the erased day

            if (IsPlausibleEquity(equityNow))
            {
                CurrentEquity = equityNow;
                HasValidEquity = true;
                PeakEquity = equityNow;
                EndOfDayHighWater = Math.Max(Config != null ? Config.StartingBalance : 0, equityNow);
                LastKnownBalance = equityNow;
            }

            if (IsPlausibleEquity(equityNow)) DayOpenBalance = equityNow - realisedNow;
            resetPending = false;
            RestartedAt = now;
            ResetSuspected = false;
        }

        /// <summary>Restore a restart time read back from the session file.</summary>
        public void SeedRestart(DateTime restartedAt)
        {
            RestartedAt = restartedAt;
        }

        /// <summary>
        /// Call on every position update for the tracked account.
        /// quantity is signed size (0 == flat).
        /// </summary>
        public void OnPosition(int signedQuantity, double realisedNow, DateTime now)
        {
            OnPosition(signedQuantity, realisedNow, now, null, null);
        }

        /// <summary>
        /// Position update that also builds the journal entry. Returns a finished
        /// entry when this update closed a round-trip, otherwise null.
        ///
        /// The context is snapshotted at ENTRY, not at exit, because that is the
        /// moment the decision was made. Recording the cushion at exit would
        /// describe the consequence rather than the choice.
        /// </summary>
        public BallastTrade OnPosition(int signedQuantity, double realisedNow, DateTime now,
                                       string instrument, string accountName)
        {
            OpenContracts = Math.Abs(signedQuantity);
            fillSinceEquity = true;

            bool flat = signedQuantity == 0;

            if (!inPosition && !flat)
            {
                // Opening a new round-trip.
                inPosition = true;
                realisedAtTradeOpen = realisedNow;

                openInstrument = instrument ?? "";
                openIsLong = signedQuantity > 0;
                openMaxContracts = OpenContracts;
                openedAt = now;
                openDailyPnl = DailyPnl;
                openMinsSinceLoss = MinutesSinceLastLoss(now);
                openPrevWasLoss = LastTradeWasLoss;
                openCommission = CurrentCommission;

                DisciplineInput snap = BuildInput(now);
                openCushion = HasValidEquity ? snap.CushionToFloor : 0;
                openFloor = snap.FloorLevel;
                openInWindow = DisciplineEngine.InSessionWindow(
                    snap.NowMinuteEt, Config.SessionStartMinute, Config.SessionEndMinute);
                openAdvice = DisciplineEngine.Evaluate(snap).Action.ToString();

                // Photograph what the trader is looking at, right now, before the
                // outcome exists to colour how they remember it.
                openImage = "";
                if (CaptureChart != null)
                {
                    try { openImage = CaptureChart(openInstrument, now, true) ?? ""; }
                    catch { openImage = ""; }
                }

                return null;
            }

            if (inPosition && !flat)
            {
                // Scaling in. The journal reports the largest size the trade ever
                // carried, since that is the risk actually taken.
                if (OpenContracts > openMaxContracts) openMaxContracts = OpenContracts;
                return null;
            }

            if (inPosition && flat)
            {
                // Round-trip complete.
                inPosition = false;

                // Unless it ended before it began, which no trade does.
                //
                // NinjaTrader replays a position's executions when Ballast
                // subscribes to an account, and they do not always arrive in the
                // order they happened. The closing SELL of a long, arriving
                // first, looks exactly like the opening of a short - so the trade
                // got written down a second time, mirrored: same money, same
                // size, direction inverted, entry and exit swapped.
                //
                // It cost more than a duplicate row. It was counted as a second
                // trade, it queued itself for tagging so he was asked to tag a
                // trade he had already tagged - and worse, the day's watched
                // total was then $880 too high, so the gap reconciler booked an
                // $884 "trade while Ballast was closed" to make the arithmetic
                // balance. One real winning trade became three trades and a loss,
                // against rules that stop him at three.
                if (now < openedAt)
                {
                    openInstrument = "";
                    openImage = "";
                    return null;
                }

                double tradePnl = realisedNow - realisedAtTradeOpen;

                TradesToday++;
                if (tradePnl < 0)
                {
                    LossesToday++;
                    LastLossAt = now;
                    LastTradeWasLoss = true;
                }
                else
                {
                    LastTradeWasLoss = false;
                }

                BallastTrade e = new BallastTrade();
                e.AccountName = accountName ?? "";
                e.Instrument = openInstrument;
                e.IsLong = openIsLong;
                e.MaxContracts = openMaxContracts;
                e.EntryTime = openedAt;
                e.ExitTime = now;
                e.Pnl = tradePnl;
                e.TradeNumberToday = TradesToday;
                e.DailyPnlBefore = openDailyPnl;
                e.CushionAtEntry = openCushion;
                e.FloorAtEntry = openFloor;
                e.MinutesSincePreviousLoss = openMinsSinceLoss;
                e.PreviousTradeWasLoss = openPrevWasLoss;
                e.InsideSessionWindow = openInWindow;
                e.AdviceAtEntry = openAdvice;
                e.Automated = Config.IsAutomated;
                e.EntryImage = openImage;

                // What the round trip cost, from the account's own running total.
                double cost = CurrentCommission - openCommission;
                e.Commission = cost > 0 ? cost : 0;

                if (CaptureChart != null)
                {
                    try { e.ExitImage = CaptureChart(openInstrument, now, false) ?? ""; }
                    catch { e.ExitImage = ""; }
                }

                return e;
            }

            return null;
        }

        public int MinutesSinceLastLoss(DateTime now)
        {
            if (!LastLossAt.HasValue) return -1;
            double mins = (now - LastLossAt.Value).TotalMinutes;
            if (mins < 0) mins = 0;
            return (int)Math.Floor(mins);
        }

        /// <summary>Build the engine input from current tracked state.</summary>
        public DisciplineInput BuildInput(DateTime nowExchange)
        {
            DisciplineInput i = new DisciplineInput();

            i.LossesToday = LossesToday;
            i.TradesToday = TradesToday;
            i.IsAutomated = Config.IsAutomated;
            i.ProfitTarget = Config.ProfitTarget;
            i.StartingBalance = Config.StartingBalance;
            i.TrailingDrawdown = Config.TrailingDrawdown;
            i.DailyPnl = DailyPnl;
            i.PeakDailyPnl = PeakDailyPnl;
            i.WorstDailyPnl = WorstDailyPnl;

            // Latched here as well as on every equity tick, because a limit that
            // was lowered mid-session must start biting against the day already
            // had rather than only against what happens next.
            if (!DailyLossLimitHit && Config.DailyLossLimit > 0
                && WorstDailyPnl <= -Math.Abs(Config.DailyLossLimit))
            {
                DailyLossLimitHit = true;
                if (!DailyLossLimitHitAt.HasValue) DailyLossLimitHitAt = nowExchange;
            }
            i.DailyLossLimitHit = DailyLossLimitHit;

            i.DailyLossLimit = Config.DailyLossLimit;
            i.DailyTarget = Config.DailyTarget;
            i.MaxLossesBeforeStop = Config.MaxLossesBeforeStop;
            i.MaxTrades = Config.MaxTrades;
            i.OpenContracts = OpenContracts;

            i.DrawdownType = Config.DrawdownType;
            i.HasValidEquity = HasValidEquity;
            i.ExecutionTelemetryHealthy = ExecutionTelemetryHealthy;
            i.ExecutionTelemetryWarning = ExecutionTelemetryWarning;
            i.CurrentEquity = CurrentEquity;

            if (HasValidEquity)
            {
                double drawdownAnchor = Config.DrawdownType == DrawdownType.Intraday
                    ? PeakEquity : EndOfDayHighWater;

                i.FloorLevel = DisciplineEngine.FloorLevel(
                    Config.StartingBalance, Config.TrailingDrawdown,
                    CurrentEquity, drawdownAnchor, Config.DrawdownType, Config.LockFloorAt);

                double firmFloor = ValidAuthoritativeFirmFloor();
                if (firmFloor > i.FloorLevel) i.FloorLevel = firmFloor;
                i.FirmFloorProviderConfirmed = firmFloor > 0 && FirmFloorProviderConfirmed;

                i.CushionToFloor = CurrentEquity - i.FloorLevel;

                i.FloorLocked = DisciplineEngine.FloorIsLocked(
                    Config.StartingBalance, Config.TrailingDrawdown,
                    CurrentEquity, drawdownAnchor, Config.DrawdownType, Config.LockFloorAt)
                    || (Config.LockFloorAt > 0 && i.FloorLevel >= Config.LockFloorAt - 0.01);

                // Throttle the advised size by how much of the drawdown is gone.
                // Only meaningful when we actually know the cushion, so it lives
                // inside the valid-equity branch.
                int baseMax = Config.BaseMaxContracts > 0 ? Config.BaseMaxContracts : Config.MaxContracts;
                i.BaseMaxContracts = baseMax;
                i.MaxContracts = RiskProfiles.ThrottledMaxContracts(
                    baseMax, Config.TrailingDrawdown, i.CushionToFloor,
                    Config.ThrottleStepPct, Config.ThrottleCutPct);
                i.SizeThrottled = i.MaxContracts < baseMax;
            }
            else
            {
                // Unknown, not zero. Signals that depend on cushion must not fire,
                // and a size we cannot justify must not be throttled on a guess.
                i.FloorLevel = 0;
                i.CushionToFloor = double.MaxValue;
                i.FloorLocked = false;
                i.FirmFloorProviderConfirmed = false;
                i.MaxContracts = Config.MaxContracts;
                i.BaseMaxContracts = Config.BaseMaxContracts > 0 ? Config.BaseMaxContracts : Config.MaxContracts;
                i.SizeThrottled = false;
            }

            i.LastTradeWasLoss = LastTradeWasLoss;
            i.MinutesSinceLastLoss = MinutesSinceLastLoss(nowExchange);
            i.CooldownMinutes = Config.CooldownMinutes;

            i.NowMinuteEt = nowExchange.Hour * 60 + nowExchange.Minute;
            i.SessionStartMinute = Config.SessionStartMinute;
            i.SessionEndMinute = Config.SessionEndMinute;

            return i;
        }
    }
}
