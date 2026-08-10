// ─────────────────────────────────────────────────────────────────────────────
// The month.
//
// "so after a month we will need to show the user some stats to know whether
// they are improving or not. What do you think?"
//
// The obvious version would do harm. A month is about a hundred trades, and at
// that sample the difference between a green month and a red one is mostly the
// market. Show a P&L curve as "improvement" and the software teaches a trader to
// feel skilled in a lucky month and broken in an unlucky one - which is the exact
// psychology the rest of Ballast exists to argue with. Win rate is worse: it goes
// up when you cut winners early and sit in losers.
//
// What actually improves inside a month, and can be measured with no luck in the
// way, is behaviour. Whether he called a trade chased. Whether he took it after
// Ballast had said stop. Whether he was inside his own cooldown, or past his own
// count. Those are counts of decisions he made. No market is in them, and no
// unlucky month can take a good one away from him.
//
// The headline metric is the one that compounds: CLEAN SESSIONS. Days where he
// kept every rule he set. Binary per day, far less noisy than a per-trade rate,
// and the thing that turns into money over a year rather than over a week.
//
// And the verdict has to be allowed to say NOTHING CHANGED, often. A chased-trade
// rate moving from 30% to 22% across a hundred trades is indistinguishable from
// noise, and a report that calls every wobble progress is worthless by its second
// issue.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballast
{
    /// <summary>What one calendar month looked like, in decisions.</summary>
    public class MonthStats
    {
        /// <summary>First day of the month this describes.</summary>
        public DateTime Month;

        /// <summary>Days he actually traded. Not calendar days.</summary>
        public int Sessions;

        /// <summary>Days he traded and broke nothing.</summary>
        public int CleanSessions;

        public int Trades;

        /// <summary>Trades he himself called chased or off plan.</summary>
        public int OffPlan;

        /// <summary>Trades opened after Ballast had already said stop, cool off or protect.</summary>
        public int AfterAStopSignal;

        /// <summary>Trades opened inside his own cooldown after a loss.</summary>
        public int InsideCooldown;

        /// <summary>Trades taken past the count he set for that account.</summary>
        public int PastTheCount;

        /// <summary>Trades that broke none of the above.</summary>
        public int Clean;

        /// <summary>What the trades that broke something came to, net.</summary>
        public double BrokenNet;

        /// <summary>What the trades that broke nothing came to, net.</summary>
        public double CleanNet;

        public double CleanSessionRate
        {
            get { return Sessions <= 0 ? -1 : (double)CleanSessions / Sessions; }
        }

        public double CleanTradeRate
        {
            get { return Trades <= 0 ? -1 : (double)Clean / Trades; }
        }

        public string Name
        {
            get { return Month.ToString("MMMM", CultureInfo.InvariantCulture); }
        }
    }

    public static class MonthReport
    {
        /// <summary>
        /// Below this many trading days a month is not a month, it is a
        /// fortnight with a holiday in it, and comparing it to anything is
        /// reading tea leaves.
        /// </summary>
        public const int MinSessions = 8;

        public static MonthStats For(List<BallastTrade> source, DateTime anyDayInMonth,
                                     int maxTrades, int cooldownMinutes)
        {
            MonthStats m = new MonthStats();
            m.Month = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1);
            if (source == null) return m;

            List<BallastTrade> book = BallastJournal.Countable(source);

            // Sessions are days he traded, and a session is clean only if every
            // trade in it was. One chased trade spoils the day - which is the
            // point: this is a measure of whole days held together, not of a
            // percentage that a good afternoon can rescue.
            List<string> days = new List<string>();
            List<bool> dayClean = new List<bool>();

            for (int i = 0; i < book.Count; i++)
            {
                BallastTrade e = book[i];
                if (e == null) continue;
                if (e.ExitTime.Year != m.Month.Year || e.ExitTime.Month != m.Month.Month) continue;

                m.Trades++;

                bool broke = BallastJournal.BrokeARule(e, maxTrades, cooldownMinutes);

                if (e.Planned == BallastJournal.Verdict_Chased
                    || e.Planned == BallastJournal.Verdict_OffPlan) m.OffPlan++;
                if (e.TakenAgainstAdvice) m.AfterAStopSignal++;
                if (cooldownMinutes > 0 && e.PreviousTradeWasLoss
                    && e.MinutesSincePreviousLoss >= 0
                    && e.MinutesSincePreviousLoss < cooldownMinutes) m.InsideCooldown++;
                if (maxTrades > 0 && e.TradeNumberToday > maxTrades) m.PastTheCount++;

                if (broke) m.BrokenNet += e.Pnl;
                else { m.Clean++; m.CleanNet += e.Pnl; }

                string key = e.ExitTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                int at = days.IndexOf(key);
                if (at < 0) { days.Add(key); dayClean.Add(!broke); }
                else if (broke) dayClean[at] = false;
            }

            m.Sessions = days.Count;
            for (int i = 0; i < dayClean.Count; i++) if (dayClean[i]) m.CleanSessions++;

            return m;
        }

        /// <summary>
        /// Did this rate really move, or is it noise?
        ///
        /// A two-proportion comparison at roughly two standard errors. It is a
        /// rule of thumb and it is described as one - Ballast is not going to
        /// tell a trader something is statistically significant, because with a
        /// hundred trades and one comparison a month that would be a bigger
        /// claim than the data can carry.
        ///
        /// What it IS good for is refusing. On a hundred trades a chased rate
        /// moving 30% to 22% does not clear this bar, and it should not, because
        /// a report that calls every wobble progress is worthless by its second
        /// issue.
        /// </summary>
        public static bool Moved(int aCount, int aOf, int bCount, int bOf)
        {
            if (aOf <= 0 || bOf <= 0) return false;

            double p1 = (double)aCount / aOf;
            double p2 = (double)bCount / bOf;

            double pooled = (double)(aCount + bCount) / (aOf + bOf);
            double se = Math.Sqrt(pooled * (1.0 - pooled) * (1.0 / aOf + 1.0 / bOf));

            // A rate of exactly 0 or exactly 1 in both months gives no spread at
            // all, and dividing by it would call any difference infinite.
            if (se <= 0.0000001) return aCount != bCount && (aCount == 0 || bCount == 0);

            return Math.Abs(p1 - p2) >= 2.0 * se;
        }

        /// <summary>
        /// Two months, read against each other. "" when there is nothing honest
        /// to say - which is a real answer and will often be the right one.
        /// </summary>
        public static string Compare(MonthStats before, MonthStats after)
        {
            if (before == null || after == null) return "";
            if (before.Sessions < MinSessions || after.Sessions < MinSessions) return "";
            if (before.Trades == 0 || after.Trades == 0) return "";

            string s = after.Name + " against " + before.Name + ".  "
                     + "Clean sessions - days you kept every rule you set - "
                     + before.CleanSessions + " of " + before.Sessions + ", then "
                     + after.CleanSessions + " of " + after.Sessions + ".";

            string moved = "";
            moved += Line("Trades you called off plan", before.OffPlan, before.Trades,
                                                        after.OffPlan, after.Trades);
            moved += Line("Taken after Ballast said stop", before.AfterAStopSignal, before.Trades,
                                                           after.AfterAStopSignal, after.Trades);
            moved += Line("Taken inside your cooldown", before.InsideCooldown, before.Trades,
                                                        after.InsideCooldown, after.Trades);
            moved += Line("Taken past your trade count", before.PastTheCount, before.Trades,
                                                         after.PastTheCount, after.Trades);

            if (moved.Length > 0) s += moved;

            // What the broken rules cost, on both sides. This is money, but it is
            // money attached to a decision rather than to what the market did -
            // which is the only kind worth putting in front of him here.
            if (before.BrokenNet < 0 || after.BrokenNet < 0)
                s += "  The trades that broke a rule came to "
                   + BallastTrade.Money(before.BrokenNet) + " in " + before.Name
                   + " and " + BallastTrade.Money(after.BrokenNet) + " in " + after.Name + ".";

            s += "  " + Verdict(before, after);

            // Said every time, because the absence of a P&L line is a decision
            // and an unexplained absence looks like an oversight.
            s += "  Your P&L is not in this. Over one month the market decides "
               + "most of it, and a number you cannot move is not a report card.";

            return s;
        }

        private static string Verdict(MonthStats before, MonthStats after)
        {
            bool sessions = Moved(before.CleanSessions, before.Sessions,
                                  after.CleanSessions, after.Sessions);
            bool trades = Moved(before.Clean, before.Trades, after.Clean, after.Trades);

            double wasS = before.CleanSessionRate, isS = after.CleanSessionRate;
            double wasT = before.CleanTradeRate, isT = after.CleanTradeRate;

            if (sessions && isS > wasS)
                return "You held more whole days together, and that is the one that compounds.";
            if (sessions && isS < wasS)
                return "Fewer whole days held together. Worth knowing before it becomes the habit.";
            if (trades && isT > wasT)
                return "Better trade by trade, though not yet enough to show in whole days.";
            if (trades && isT < wasT)
                return "Worse trade by trade. Nothing has gone wrong with the market - this is you.";

            return "No real change. Two months is not much evidence, and a report that called "
                 + "every wobble progress would be worth nothing by its second issue - so it "
                 + "says nothing until something genuinely moves.";
        }

        private static string Line(string what, int aCount, int aOf, int bCount, int bOf)
        {
            if (aCount == bCount) return "";
            if (!Moved(aCount, aOf, bCount, bOf)) return "";
            return "  " + what + ": " + aCount + " to " + bCount + ".";
        }
    }
}
