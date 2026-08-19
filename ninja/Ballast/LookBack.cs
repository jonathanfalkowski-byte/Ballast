// ─────────────────────────────────────────────────────────────────────────────
// One trade back.
//
// "i dont actually look at the trades i did yesterday...so does looking at the
// previous days trade help you trade better? Does the psychologiists say that?
// if it does, then how do we make the user look at their past trades?"
//
// It does, with a caveat that decides the whole design.
//
// Di Stefano, Gino, Pisano and Staats (Learning by Thinking, ten studies, 4,340
// participants) found that reflecting on accumulated experience can outperform
// simply getting more practice, and that the gain is largest at the BEGINNING of
// the learning curve - which is exactly where a trader learning his own habits
// is standing.
//
// But Kluger and DeNisi's meta-analysis of feedback interventions found that
// over a third of them made performance WORSE - 38% of effects negative, 33%
// after excluding outliers - and the thing that separates the helpful kind from
// the harmful kind is where the feedback points attention. Task-focused feedback
// improves performance; SELF-focused feedback degrades it, because attention
// spent defending yourself is attention not spent on the work.
//
// So: never a sentence about him. Only sentences about a trade.
//
// And the answer to "how do we make him look" is to stop asking him to. Browsing
// a table of yesterday is a chore with no payoff in the moment, which is why it
// does not happen. One trade, in the card he already reads, with the picture
// Ballast already saved. Ten seconds.
//
// The trade chosen is the one that MISLEADS - where the result disagrees with
// the decision. A trade that broke a rule and won is the most expensive row in
// any journal, because it teaches the habit that will eventually cost the
// account. A trade taken by the book that lost is the other half: left alone it
// quietly teaches that the plan does not work.
//
// And the outcome is withheld until he has answered, because outcome bias is the
// specific way reviewing your own trades teaches the wrong lesson. His own
// journal has the case: "that trade is not guaranteed nor proven so i should
// test it out before moving it to live trading....but it just worked."
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballast
{
    public class LookBackPick
    {
        public BallastTrade Trade;

        /// <summary>True when this trade broke a rule and still made money.</summary>
        public bool RewardedAMistake;

        /// <summary>True when it was taken by the book and lost.</summary>
        public bool PunishedThePlan;
    }

    public static class LookBack
    {
        /// <summary>How far back to look. Beyond this the charts have been deleted.</summary>
        public const int Days = 10;

        /// <summary>Identifies a trade across restarts without needing an id on it.</summary>
        private static bool Skip(List<string> accounts, string name)
        {
            if (accounts == null || string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < accounts.Count; i++)
                if (string.Equals(accounts[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string KeyOf(BallastTrade e)
        {
            if (e == null) return "";
            return (e.AccountName == null ? "" : e.AccountName) + "|"
                 + e.ExitTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The most misleading trade of the last few days that he has not already
        /// been shown. Null when there is nothing worth putting in front of him -
        /// which is a real answer, not a failure.
        /// </summary>
        public static LookBackPick Pick(List<BallastTrade> source, DateTime now,
                                        int maxTrades, int cooldownMinutes,
                                        List<string> alreadyShown)
        {
            return Pick(source, now, maxTrades, cooldownMinutes, alreadyShown, null);
        }

        /// <summary>
        /// As above, ignoring any account in `skipAccounts` entirely.
        ///
        /// "dont post any trades in this window if they are from a bot"
        ///
        /// The row-level Automated flag cannot do this on its own. It is stamped
        /// when the trade is written, from the account's setting AT THAT MOMENT
        /// - so an account that becomes a bot afterwards leaves a trail of rows
        /// marked hand-traded. Sim110 has twenty rows in his journal and exactly
        /// two of them are flagged; the other fifteen were written before he
        /// ticked it. Every one of those was eligible for this card.
        ///
        /// And the card is the wrong place for them whichever way they are
        /// flagged. It asks "would you take it again", which is not a question
        /// about a trade nobody chose.
        /// </summary>
        public static LookBackPick Pick(List<BallastTrade> source, DateTime now,
                                        int maxTrades, int cooldownMinutes,
                                        List<string> alreadyShown,
                                        List<string> skipAccounts)
        {
            if (source == null) return null;

            DateTime from = now.Date.AddDays(-Days);

            LookBackPick bestRewarded = null, bestPunished = null;
            double rewardedBy = 0, punishedBy = 0;

            List<BallastTrade> book = BallastJournal.Countable(source);

            for (int i = 0; i < book.Count; i++)
            {
                BallastTrade e = book[i];
                if (e == null) continue;

                if (skipAccounts != null && Skip(skipAccounts, e.AccountName)) continue;

                // Today is not hindsight, it is this morning. A trade needs to
                // have been slept on before looking at it teaches anything.
                if (e.ExitTime.Date >= now.Date) continue;
                if (e.ExitTime.Date < from) continue;

                if (alreadyShown != null && alreadyShown.Contains(KeyOf(e))) continue;

                bool broke = BallastJournal.BrokeARule(e, maxTrades, cooldownMinutes);

                if (broke && e.Pnl > 0)
                {
                    if (bestRewarded == null || e.Pnl > rewardedBy)
                    {
                        rewardedBy = e.Pnl;
                        bestRewarded = new LookBackPick();
                        bestRewarded.Trade = e;
                        bestRewarded.RewardedAMistake = true;
                    }
                }
                else if (!broke && e.Pnl < 0 && e.Planned == BallastJournal.Verdict_ByTheBook)
                {
                    if (bestPunished == null || e.Pnl < punishedBy)
                    {
                        punishedBy = e.Pnl;
                        bestPunished = new LookBackPick();
                        bestPunished.Trade = e;
                        bestPunished.PunishedThePlan = true;
                    }
                }
            }

            // A rewarded mistake outranks a punished plan. It is the one that
            // actively builds the habit, and the habit is what costs the account
            // later - a losing trade taken correctly costs only its stop.
            return bestRewarded != null ? bestRewarded : bestPunished;
        }

        /// <summary>
        /// What to ask, with the outcome still hidden.
        ///
        /// Every sentence here is about the trade. None of them is about him:
        /// that is the line the feedback research draws between an intervention
        /// that helps and one that costs performance, and it is not a style
        /// preference.
        /// </summary>
        public static string Question(LookBackPick p, int maxTrades, int cooldownMinutes)
        {
            if (p == null || p.Trade == null) return "";
            BallastTrade e = p.Trade;

            string when = e.EntryTime.ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture);
            string what = e.Instrument + (e.MaxContracts > 0 ? ", " + e.MaxContracts
                        + (e.MaxContracts == 1 ? " contract" : " contracts") : "");

            string s = when + " - " + what + ".";

            // The rule it broke, stated as a fact about the trade.
            if (maxTrades > 0 && e.TradeNumberToday > maxTrades)
                s += "  Trade number " + e.TradeNumberToday + " of a day you had capped at "
                   + maxTrades + ".";
            else if (cooldownMinutes > 0 && e.PreviousTradeWasLoss
                     && e.MinutesSincePreviousLoss >= 0
                     && e.MinutesSincePreviousLoss < cooldownMinutes)
                s += "  Taken " + e.MinutesSincePreviousLoss
                   + (e.MinutesSincePreviousLoss == 1 ? " minute" : " minutes")
                   + " after a loss, against a cooldown of " + cooldownMinutes + ".";
            else if (e.TakenAgainstAdvice)
                s += "  Taken while Ballast was already saying stop.";
            else if (e.Planned == BallastJournal.Verdict_Chased)
                s += "  You tagged this one chased.";
            else if (e.Planned == BallastJournal.Verdict_OffPlan)
                s += "  You tagged this one off plan.";

            if (e.Setup.Length > 0) s += "  Setup: " + e.Setup + ".";

            s += "\n\n";
            s += p.RewardedAMistake
                ? "This is the entry only. Looking at just this - would you take it again?"
                : "This one was by the book. This is the entry only - looking at just this, "
                  + "was there anything here to see?";

            return s;
        }

        /// <summary>
        /// What actually happened, once he has answered - and the reason the
        /// trade was chosen.
        /// </summary>
        public static string Reveal(LookBackPick p)
        {
            if (p == null || p.Trade == null) return "";
            BallastTrade e = p.Trade;

            string s = e.Pnl >= 0
                ? "It made " + BallastTrade.Money(e.Pnl) + "."
                : "It cost " + BallastTrade.Money(-e.Pnl) + ".";

            if (p.RewardedAMistake)
                s += "  Which is why this one is here. A trade that broke a rule and paid is the "
                   + "most expensive row in any journal - not for what it cost, but for what it "
                   + "teaches. Nothing about the entry got better because the market moved your "
                   + "way afterwards.";
            else
                s += "  Which is why this one is here. Kept to the plan and still lost - that is "
                   + "the cost of doing it properly, not evidence the plan is wrong. A rule that "
                   + "only ever wins was never a rule, it was a lucky run.";

            if (e.Note.Length > 0) s += "\n\nWhat you wrote at the time: \"" + e.Note + "\"";

            return s;
        }
    }
}
