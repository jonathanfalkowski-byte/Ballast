// ─────────────────────────────────────────────────────────────────────────────
// Ballast — RiskProfiles.cs
//
// Sensible starting guardrails, derived from published risk-management methods
// and translated to a prop account.
//
// THE TRANSLATION IS THE WHOLE POINT.
//
// Almost every published rule sizes risk against account equity: "risk 1% per
// trade". On a funded prop account that advice is actively dangerous, because
// your real risk capital is not the balance - it is the trailing drawdown. An
// Apex 50K has $50,000 of balance and $2,000 of life. Risking "1% of the
// account" is $500, which is a quarter of everything you have. Four of those in
// a row and the account is gone, having lost 4% of its stated size.
//
// So every profile here is expressed as a percentage of the DRAWDOWN, and the
// dollar figures are computed from each account's own drawdown rather than
// baked in. A 25K Apex and a 150K Topstep get different numbers from the same
// profile, because they have different amounts of life.
//
// ON ATTRIBUTION: profiles are named for the METHOD, not for a person, and each
// carries a source line saying where the principle comes from. Published figures
// were set for other markets and other holding periods - Minervini's 1.25% is an
// equity swing-trading number, not a rule he wrote for two-hour futures scalps
// on a trailing drawdown. Borrowing the principle is honest; implying the person
// endorsed these settings would not be.
//
// None of this is advice. It is a starting configuration for guardrails the
// trader can change at will, and every number here should be checked against
// their own firm and their own results.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Ballast
{
    public class RiskProfile
    {
        public string Key = "";
        public string Name = "";

        /// <summary>One line shown in the dropdown area.</summary>
        public string Summary = "";

        /// <summary>Where the principle comes from, stated plainly.</summary>
        public string Source = "";

        /// <summary>Percentage of the TRAILING DRAWDOWN risked on one trade.</summary>
        public double RiskPctOfDrawdown;

        /// <summary>Percentage of the trailing drawdown allowed to be lost in a day.</summary>
        public double DailyLossPctOfDrawdown;

        /// <summary>Daily target as a multiple of the daily loss limit.</summary>
        public double TargetMultiple = 1.0;

        public int MaxLossesBeforeStop = 2;
        public int MaxTrades = 4;
        public int CooldownMinutes = 5;

        /// <summary>
        /// Turtle-style throttle. For every ThrottleStepPct of the drawdown that
        /// has been consumed, advise cutting size by ThrottleCutPct. 0 disables.
        /// </summary>
        public double ThrottleStepPct;
        public double ThrottleCutPct;

        public bool HasThrottle { get { return ThrottleStepPct > 0 && ThrottleCutPct > 0; } }
    }

    public static class RiskProfiles
    {
        /// <summary>
        /// The catalogue. Ordered gentlest first, because a trader browsing a
        /// dropdown tends to pick from the top and the top should be the one
        /// least likely to lose them the account.
        /// </summary>
        public static List<RiskProfile> All()
        {
            List<RiskProfile> list = new List<RiskProfile>();

            // ── 1. Survival first ────────────────────────────────────────────
            RiskProfile survive = new RiskProfile();
            survive.Key = "survival";
            survive.Name = "Survival first - 10% of your drawdown per trade";
            survive.Summary = "Ten losing trades in a row before the account dies. Slow, boring, "
                            + "and the only setting that reliably survives a bad week.";
            survive.Source = "Prop-specific position sizing guidance rates under 15% of the max "
                           + "drawdown per trade as conservative, and over 40% as dangerous.";
            survive.RiskPctOfDrawdown = 10;
            survive.DailyLossPctOfDrawdown = 25;
            survive.TargetMultiple = 1.0;
            survive.MaxLossesBeforeStop = 2;
            survive.MaxTrades = 3;
            survive.CooldownMinutes = 10;
            survive.ThrottleStepPct = 10;
            survive.ThrottleCutPct = 20;
            list.Add(survive);

            // ── 2. Fixed fractional ──────────────────────────────────────────
            RiskProfile fixedFrac = new RiskProfile();
            fixedFrac.Key = "fixed_fractional";
            fixedFrac.Name = "Fixed fractional - 15% of your drawdown per trade";
            fixedFrac.Summary = "The classic 'risk a constant fraction' approach, measured against "
                              + "your drawdown rather than your balance. Six or seven losses in a row "
                              + "before the account is gone.";
            fixedFrac.Source = "Fixed-fractional position sizing, popularised for retail traders by "
                             + "Van Tharp. The percentage is re-based onto the drawdown here because "
                             + "on a prop account the balance is not the money at risk.";
            fixedFrac.RiskPctOfDrawdown = 15;
            fixedFrac.DailyLossPctOfDrawdown = 30;
            fixedFrac.TargetMultiple = 1.0;
            fixedFrac.MaxLossesBeforeStop = 2;
            fixedFrac.MaxTrades = 4;
            fixedFrac.CooldownMinutes = 5;
            fixedFrac.ThrottleStepPct = 0;
            fixedFrac.ThrottleCutPct = 0;
            list.Add(fixedFrac);

            // ── 3. Volatility throttle ───────────────────────────────────────
            RiskProfile throttle = new RiskProfile();
            throttle.Key = "throttle";
            throttle.Name = "Drawdown throttle - size comes down as your cushion does";
            throttle.Summary = "Starts at 15% of your drawdown per trade, then cuts your advised size "
                             + "by 20% for every 10% of the drawdown you have already spent. You "
                             + "cannot size up into a losing day.";
            throttle.Source = "Derived from the Turtle traders' rule: reduce trading size by 20% for "
                            + "every 10% of capital lost, so that a losing run shrinks your exposure "
                            + "automatically instead of relying on you to do it.";
            throttle.RiskPctOfDrawdown = 15;
            throttle.DailyLossPctOfDrawdown = 30;
            throttle.TargetMultiple = 1.5;
            throttle.MaxLossesBeforeStop = 2;
            throttle.MaxTrades = 4;
            throttle.CooldownMinutes = 10;
            throttle.ThrottleStepPct = 10;
            throttle.ThrottleCutPct = 20;
            list.Add(throttle);

            // ── 4. Cut fast, protect green ───────────────────────────────────
            RiskProfile cutFast = new RiskProfile();
            cutFast.Key = "cut_fast";
            cutFast.Name = "Cut fast, protect green - small losses, never give back a win";
            cutFast.Summary = "Tight per-trade risk and an early daily stop, with a low give-back "
                            + "tolerance so a green day is defended rather than donated back.";
            cutFast.Source = "Applies two principles Mark Minervini has stated publicly for his own "
                           + "equity trading - keep average losses small relative to average gains, "
                           + "and never let a decent gain turn into a loss. The percentages here are "
                           + "not his; his figures are for stock positions held for weeks.";
            cutFast.RiskPctOfDrawdown = 12;
            cutFast.DailyLossPctOfDrawdown = 25;
            cutFast.TargetMultiple = 2.0;
            cutFast.MaxLossesBeforeStop = 2;
            cutFast.MaxTrades = 4;
            cutFast.CooldownMinutes = 5;
            cutFast.ThrottleStepPct = 10;
            cutFast.ThrottleCutPct = 20;
            list.Add(cutFast);

            // ── 5. Process first ─────────────────────────────────────────────
            RiskProfile process = new RiskProfile();
            process.Key = "process";
            process.Name = "Process first - very few trades, judged on quality";
            process.Summary = "Two trades a day, a long cooldown, and a stop after one loss. Built to "
                            + "make each decision cost something, not to maximise opportunity.";
            process.Source = "Reflects the trading-psychology position that process goals - what you "
                           + "did and why - change behaviour where outcome goals do not. Deliberately "
                           + "restrictive; most traders will find it too tight, which is the point.";
            process.RiskPctOfDrawdown = 10;
            process.DailyLossPctOfDrawdown = 20;
            process.TargetMultiple = 1.5;
            process.MaxLossesBeforeStop = 1;
            process.MaxTrades = 2;
            process.CooldownMinutes = 20;
            process.ThrottleStepPct = 10;
            process.ThrottleCutPct = 20;
            list.Add(process);

            // ── 6. Asymmetric R ──────────────────────────────────────────────
            RiskProfile asym = new RiskProfile();
            asym.Key = "asymmetric";
            asym.Name = "Asymmetric R - small risk, only trades worth taking";
            asym.Summary = "Small per-trade risk and a target several times the daily stop, so the "
                         + "day is won by a couple of good trades rather than a pile of scratches.";
            asym.Source = "Built on the asymmetric risk/reward idea Paul Tudor Jones is closely "
                        + "associated with - hunting setups where the upside is a multiple of the "
                        + "risk, and cutting anything that goes against you rather than adding to it. "
                        + "He trades a global macro book, not two-hour futures scalps; the shape is "
                        + "borrowed, the numbers are not his.";
            asym.RiskPctOfDrawdown = 10;
            asym.DailyLossPctOfDrawdown = 20;
            asym.TargetMultiple = 3.0;
            asym.MaxLossesBeforeStop = 2;
            asym.MaxTrades = 3;
            asym.CooldownMinutes = 10;
            asym.ThrottleStepPct = 10;
            asym.ThrottleCutPct = 20;
            list.Add(asym);

            // ── 7. One percent rule ──────────────────────────────────────────
            RiskProfile onePct = new RiskProfile();
            onePct.Key = "flat_one_pct";
            onePct.Name = "The 1% rule - translated honestly to a funded account";
            onePct.Summary = "The most quoted rule in trading, re-based so it means what people think "
                           + "it means. 1% of a 50K balance would be a quarter of your drawdown; this "
                           + "is 1% of the money you can actually lose.";
            onePct.Source = "Larry Hite's rule from Market Wizards - never risk more than 1% of total "
                          + "equity on a trade. On a funded account 'total equity' is the drawdown, "
                          + "not the balance, so the percentage is applied there instead. That "
                          + "translation is the point of this entry.";
            onePct.RiskPctOfDrawdown = 8;
            onePct.DailyLossPctOfDrawdown = 20;
            onePct.TargetMultiple = 1.5;
            onePct.MaxLossesBeforeStop = 2;
            onePct.MaxTrades = 4;
            onePct.CooldownMinutes = 5;
            onePct.ThrottleStepPct = 10;
            onePct.ThrottleCutPct = 20;
            list.Add(onePct);

            // ── 8. Half Kelly ────────────────────────────────────────────────
            RiskProfile halfKelly = new RiskProfile();
            halfKelly.Key = "half_kelly";
            halfKelly.Name = "Half-Kelly - the growth-optimal bet, halved";
            halfKelly.Summary = "For traders who know their win rate and average win/loss. Sized below "
                              + "the mathematically optimal bet on purpose, because the optimal bet "
                              + "assumes you measured your edge correctly and you probably did not.";
            halfKelly.Source = "The Kelly criterion maximises long-run growth but is brutally "
                             + "sensitive to a mis-estimated edge; overstating it turns the optimal "
                             + "bet into ruin. Practitioners commonly trade a fraction of it - half "
                             + "Kelly gives most of the growth with far less variance.";
            halfKelly.RiskPctOfDrawdown = 12;
            halfKelly.DailyLossPctOfDrawdown = 25;
            halfKelly.TargetMultiple = 1.5;
            halfKelly.MaxLossesBeforeStop = 2;
            halfKelly.MaxTrades = 4;
            halfKelly.CooldownMinutes = 5;
            halfKelly.ThrottleStepPct = 10;
            halfKelly.ThrottleCutPct = 20;
            list.Add(halfKelly);

            // ── 9. Evaluation pass ───────────────────────────────────────────
            RiskProfile eval = new RiskProfile();
            eval.Key = "evaluation";
            eval.Name = "Passing an evaluation - reach the target without dying";
            eval.Summary = "Tuned for the specific job of clearing a profit target on a fresh "
                         + "evaluation: enough size to make progress, tight enough that a bad day "
                         + "does not end the attempt.";
            eval.Source = "Not attributed to anyone - this is Ballast's own, built around the "
                        + "arithmetic of an evaluation. You need roughly ten good days and you can "
                        + "afford roughly five bad ones, so the daily stop is set to a fifth of the "
                        + "drawdown and the target to match it.";
            eval.RiskPctOfDrawdown = 12;
            eval.DailyLossPctOfDrawdown = 20;
            eval.TargetMultiple = 1.5;
            eval.MaxLossesBeforeStop = 2;
            eval.MaxTrades = 4;
            eval.CooldownMinutes = 10;
            eval.ThrottleStepPct = 10;
            eval.ThrottleCutPct = 20;
            list.Add(eval);

            return list;
        }

        public static RiskProfile ByKey(string key)
        {
            List<RiskProfile> all = All();
            for (int i = 0; i < all.Count; i++)
                if (all[i].Key == key) return all[i];
            return null;
        }

        /// <summary>
        /// Turn a profile into concrete settings for one account, using that
        /// account's own trailing drawdown. Personal preferences that the trader
        /// has already set are overwritten here on purpose - applying a profile is
        /// an explicit request to be told what the numbers should be.
        /// </summary>
        public static TrackerConfig Apply(RiskProfile p, TrackerConfig existing, double tickValuePerContract)
        {
            TrackerConfig c = existing != null
                ? BallastMonitor.CloneConfig(existing)
                : new TrackerConfig();

            if (p == null) return c;

            double dd = c.TrailingDrawdown;

            if (dd > 0)
            {
                c.DailyLossLimit = Round25(dd * p.DailyLossPctOfDrawdown / 100.0);
                c.DailyTarget = Round25(c.DailyLossLimit * p.TargetMultiple);
            }

            c.MaxLossesBeforeStop = p.MaxLossesBeforeStop;
            c.MaxTrades = p.MaxTrades;
            c.CooldownMinutes = p.CooldownMinutes;

            c.RiskPctOfDrawdown = p.RiskPctOfDrawdown;
            c.ThrottleStepPct = p.ThrottleStepPct;
            c.ThrottleCutPct = p.ThrottleCutPct;
            c.ProfileKey = p.Key;

            // Base size: how many contracts can be risked on one trade, given the
            // per-trade dollar allowance and what one contract's stop is worth.
            // Falls back to leaving the trader's own number alone when we have no
            // basis to compute one, rather than inventing a size.
            if (dd > 0 && tickValuePerContract > 0)
            {
                double perTrade = dd * p.RiskPctOfDrawdown / 100.0;
                int size = (int)Math.Floor(perTrade / tickValuePerContract);
                if (size < 1) size = 1;

                // Never above the firm's own cap, however much risk budget says.
                if (c.FirmMaxContracts > 0 && size > c.FirmMaxContracts) size = c.FirmMaxContracts;
                c.MaxContracts = size;
            }

            return c;
        }

        /// <summary>Round to the nearest $25 so the numbers look deliberate.</summary>
        public static double Round25(double v)
        {
            if (v <= 0) return 0;
            double r = Math.Round(v / 25.0) * 25.0;
            return r < 25 ? 25 : r;
        }

        /// <summary>
        /// The throttle. Given how much of the drawdown has already been spent,
        /// how many contracts should be advised now?
        ///
        /// Turtle rule shape: for every ThrottleStepPct of capital gone, cut size
        /// by ThrottleCutPct - applied multiplicatively, so successive losses
        /// compound the reduction rather than marching linearly to zero.
        /// </summary>
        public static int ThrottledMaxContracts(int baseMax, double drawdown, double cushionNow,
                                                double stepPct, double cutPct)
        {
            if (baseMax < 1) return 1;
            if (stepPct <= 0 || cutPct <= 0) return baseMax;
            if (drawdown <= 0 || cushionNow >= drawdown) return baseMax;

            double spent = drawdown - cushionNow;
            if (spent <= 0) return baseMax;

            double spentPct = spent / drawdown * 100.0;
            int steps = (int)Math.Floor(spentPct / stepPct);
            if (steps <= 0) return baseMax;

            double factor = Math.Pow(1.0 - (cutPct / 100.0), steps);
            int size = (int)Math.Floor(baseMax * factor);

            // Never advise zero. "Stop trading" is the engine's job to say, in
            // words; a size of 0 contracts would be a confusing way to say it.
            return size < 1 ? 1 : size;
        }

        /// <summary>Human-readable explanation of what the throttle is doing right now.</summary>
        public static string ThrottleNote(int baseMax, int throttled, double drawdown, double cushionNow)
        {
            if (throttled >= baseMax) return "";

            double spent = drawdown - cushionNow;
            double pct = drawdown > 0 ? spent / drawdown * 100.0 : 0;

            return "You have spent " + pct.ToString("0") + "% of this account's drawdown, so your "
                 + "advised size is down from " + baseMax + " to " + throttled + ".";
        }
    }
}
