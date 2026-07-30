// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastTracker.cs
//
// Turns raw NinjaTrader account events into the flat state the DisciplineEngine
// needs. Deliberately separated from the UI so the counting logic stays testable
// and so a bug here can't take the window down.
//
// How a "trade" is counted: we watch the position for this account. When it goes
// from non-flat to flat, one round-trip is complete. The trade's P&L is the change
// in realised P&L across that round-trip. This avoids trying to pair individual
// executions, which is fragile with partial fills and scale-outs.
// ─────────────────────────────────────────────────────────────────────────────

using System;

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

        /// <summary>The firm's own hard contract cap, 0 if unknown. Never exceeded.</summary>
        public int FirmMaxContracts = 0;

        /// <summary>
        /// Which generation THIS account belongs to. Per account, because a trader
        /// can hold legacy and current side by side, and the same balance means a
        /// different drawdown in each.
        /// </summary>
        public AccountGeneration Generation = AccountGeneration.Auto;

        public int SessionStartMinute = 570; // 09:30
        public int SessionEndMinute = 690;   // 11:30
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
        public DateTime? LastLossAt;
        public bool LastTradeWasLoss;

        public int OpenContracts;
        public double PeakEquity;        // for intraday trailing: highest equity seen
        public double CurrentEquity;

        /// <summary>
        /// False until we've seen a believable equity reading. A disconnected or
        /// not-yet-loaded account reports 0, and computing a cushion from that
        /// produces an alarming nonsense number - so we report "no data" instead.
        /// </summary>
        public bool HasValidEquity;

        private double sessionStartRealised;
        private bool haveBaseline;
        private bool inPosition;
        private double realisedAtTradeOpen;
        private DateTime sessionDate = DateTime.MinValue.Date;

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
        private string openAdvice = "";
        private string openImage = "";

        /// <summary>
        /// Set by the host so the tracker can photograph the chart without knowing
        /// anything about NinjaTrader or WPF. Null in tests, which is the point:
        /// the counting logic stays testable and a capture bug cannot reach it.
        /// </summary>
        public Func<string, DateTime, bool, string> CaptureChart;

        /// <summary>Reset accumulators when a new trading day starts.</summary>
        public void EnsureSession(DateTime now, double realisedNow, double equityNow)
        {
            if (sessionDate != now.Date)
            {
                sessionDate = now.Date;
                TradesToday = 0;
                LossesToday = 0;
                DailyPnl = 0;
                PeakDailyPnl = 0;
                LastLossAt = null;
                LastTradeWasLoss = false;
                sessionStartRealised = realisedNow;
                haveBaseline = true;
                inPosition = false;
                bool ok = IsPlausibleEquity(equityNow);
                PeakEquity = ok ? equityNow : 0;
                CurrentEquity = ok ? equityNow : 0;
                HasValidEquity = ok;
            }
            else if (!haveBaseline)
            {
                sessionStartRealised = realisedNow;
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
            if (!IsPlausibleEquity(equityNow))
            {
                HasValidEquity = false;
                return;
            }

            HasValidEquity = true;
            CurrentEquity = equityNow;

            // Heal a peak that was poisoned before we could judge it.
            if (!IsPlausibleEquity(PeakEquity)) PeakEquity = equityNow;

            if (equityNow > PeakEquity) PeakEquity = equityNow;

            if (haveBaseline)
            {
                DailyPnl = realisedNow - sessionStartRealised;
                if (DailyPnl > PeakDailyPnl) PeakDailyPnl = DailyPnl;
            }
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

                DisciplineInput snap = BuildInput(now);
                openCushion = HasValidEquity ? snap.CushionToFloor : 0;
                openFloor = snap.FloorLevel;
                openInWindow = snap.NowMinuteEt >= Config.SessionStartMinute
                            && snap.NowMinuteEt <= Config.SessionEndMinute;
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
            i.DailyPnl = DailyPnl;
            i.PeakDailyPnl = PeakDailyPnl;

            i.DailyLossLimit = Config.DailyLossLimit;
            i.DailyTarget = Config.DailyTarget;
            i.MaxLossesBeforeStop = Config.MaxLossesBeforeStop;
            i.MaxTrades = Config.MaxTrades;
            i.OpenContracts = OpenContracts;

            i.DrawdownType = Config.DrawdownType;
            i.HasValidEquity = HasValidEquity;
            i.CurrentEquity = CurrentEquity;

            if (HasValidEquity)
            {
                i.FloorLevel = DisciplineEngine.FloorLevel(
                    Config.StartingBalance, Config.TrailingDrawdown,
                    CurrentEquity, PeakEquity, Config.DrawdownType, Config.LockFloorAt);

                i.CushionToFloor = CurrentEquity - i.FloorLevel;

                i.FloorLocked = DisciplineEngine.FloorIsLocked(
                    Config.StartingBalance, Config.TrailingDrawdown,
                    CurrentEquity, PeakEquity, Config.DrawdownType, Config.LockFloorAt);

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
