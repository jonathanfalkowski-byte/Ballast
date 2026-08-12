// ─────────────────────────────────────────────────────────────────────────────
// The consistency rule.
//
// "if your best single day is more than half your total profit, you can't
// withdraw until you dilute it with more days"
//
// This is the only prop rule that punishes a GOOD day, which is why it needs to
// be in here and why nobody works it out in the moment. Every other number
// Ballast shows says the same thing when the day goes well: you are up, protect
// it. This one says stop, and it says stop precisely when stopping feels most
// like leaving money on the table.
//
// Nothing is lost when it is crossed. That matters, and it shapes how this is
// allowed to speak. A daily loss limit crossed is money gone; a consistency
// ceiling crossed is a payout DEFERRED until more days are added underneath it.
// So this warns, loudly, with the arithmetic on screen - and it never locks an
// account out of a winning trade to protect a withdrawal date.
//
// The firm's own formula, from Apex's help centre:
//
//     Highest Profit Day ÷ 0.3 = Minimum Total Profit Required
//
// Inverted, which is the form a trader can act on while the day is still open:
// if today is going to be the biggest day, it may earn up to
//
//     ceiling = r × P / (1 - r)
//
// where P is net profit since the last approved payout, not counting today, and
// r is the firm's fraction. At 30% and $2,000 banked that is $857. At 50% it is
// $2,000. Past it, the payout is not lost - it is postponed until total profit
// reaches best ÷ r.
//
// TWO NUMBERS, TWO PERCENTAGES, AND THE DIFFERENCE IS HIS OWN ACCOUNTS.
//
// Apex runs 50% on current Performance accounts and 30% on Legacy ones, and a
// 250K Apex account can only be legacy - 4.0 stops at 150K. Both of his funded
// accounts are 250K. A tool that hard-coded the 50% it read on a blog would
// have told him a $1,000 day was fine on a $2,000 base when 30% blocks it at
// $857. So the percentage comes from the rule book, per plan, and an account
// whose firm has no figures published in the book gets no ceiling at all
// rather than a borrowed one.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Ballast
{
    /// <summary>
    /// What a firm publishes about getting paid. All zeroes means "this firm's
    /// payout terms are not in the rule book", which is a real answer and is
    /// reported as one - never as "no rule".
    /// </summary>
    public class PayoutRules
    {
        /// <summary>
        /// The share of total profit one day may not reach, as a percentage.
        /// 30 for Apex legacy, 50 for Apex 4.0 performance accounts. Zero means
        /// no published consistency rule.
        /// </summary>
        public double ConsistencyPct;

        /// <summary>Profit a day needs to make to count towards the day count.</summary>
        public double QualifyingDayMinimum;

        /// <summary>How many qualifying days before a payout can be requested.</summary>
        public int QualifyingDaysRequired;

        /// <summary>Smallest request the firm will take.</summary>
        public double MinimumPayout;

        /// <summary>
        /// How many payouts this account gets before the terms change - Apex's
        /// ladder runs to six, after which the consistency rule stops applying.
        /// </summary>
        public int MaxPayouts;

        public bool HasConsistencyRule { get { return ConsistencyPct > 0 && ConsistencyPct < 100; } }
        public bool Known { get { return ConsistencyPct > 0 || QualifyingDaysRequired > 0; } }

        public double Fraction { get { return ConsistencyPct / 100.0; } }
    }

    /// <summary>One trading day's net result on one account.</summary>
    public class PayoutDay
    {
        public DateTime Day;

        /// <summary>Net of commission, because that is what the firm counts.</summary>
        public double Pnl;
    }

    /// <summary>
    /// Where this account stands against its firm's payout terms. Every figure
    /// here is derived - nothing is stored, so a corrected journal or a
    /// corrected payout date changes all of it at once.
    /// </summary>
    public class PayoutStanding
    {
        public bool Known;

        /// <summary>Days counted, and of those, days big enough to qualify.</summary>
        public int Days;
        public int QualifyingDays;
        public int DaysStillNeeded;

        /// <summary>Net profit since the baseline. Losing days are in it.</summary>
        public double NetProfit;

        /// <summary>The biggest single winning day, and when it was.</summary>
        public double BestDay;
        public DateTime BestDayOn;

        /// <summary>Best day as a share of net profit, 0-1. Zero when net profit is not positive.</summary>
        public double Share;

        /// <summary>True when a payout requested right now would be refused on consistency.</summary>
        public bool Blocked;

        /// <summary>
        /// Total profit still to be made before the existing best day stops
        /// blocking. Apex's own formula: best ÷ r, less what is already there.
        /// </summary>
        public double ProfitToUnblock;

        /// <summary>
        /// The most today may still earn before today itself becomes the
        /// blocking day. Negative or zero means any further profit today will
        /// defer the payout.
        /// </summary>
        public double CeilingToday;

        /// <summary>True once there is enough here for the ceiling to be worth showing.</summary>
        public bool CeilingWorthShowing;

        /// <summary>Set when today has already gone past the ceiling.</summary>
        public bool PastCeiling;

        /// <summary>Enough days, enough money, and consistency satisfied.</summary>
        public bool CouldRequestNow;
    }

    public static class PayoutBook
    {
        /// <summary>
        /// Group one account's journal into trading days, net of commission,
        /// from the baseline forward.
        ///
        /// Commission is subtracted because the firm's figure is net and the
        /// journal's is not - the day Sim103 read -$120 in the journal, the
        /// account read -$128.72, and a consistency percentage computed off the
        /// wrong one is wrong in whichever direction the volume happened to go.
        ///
        /// Reconstructed rows are included. They are money the account actually
        /// made or lost, and the firm counts them whether Ballast watched them
        /// or not.
        /// </summary>
        public static List<PayoutDay> Days(List<BallastTrade> all, string account,
                                           DateTime since, int tradingDayResetMinute)
        {
            List<PayoutDay> days = new List<PayoutDay>();
            if (all == null) return days;

            Dictionary<DateTime, double> byDay = new Dictionary<DateTime, double>();
            for (int i = 0; i < all.Count; i++)
            {
                BallastTrade t = all[i];
                if (t == null) continue;
                if (!string.IsNullOrEmpty(account)
                    && !string.Equals(t.AccountName, account, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime key = DayOf(t.ExitTime, tradingDayResetMinute);
                if (since != DateTime.MinValue.Date && key < since.Date) continue;

                double net = t.Pnl - t.Commission;
                if (byDay.ContainsKey(key)) byDay[key] = byDay[key] + net;
                else byDay[key] = net;
            }

            foreach (KeyValuePair<DateTime, double> kv in byDay)
            {
                PayoutDay d = new PayoutDay();
                d.Day = kv.Key;
                d.Pnl = kv.Value;
                days.Add(d);
            }

            days.Sort(CompareDays);
            return days;
        }

        private static int CompareDays(PayoutDay a, PayoutDay b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            return a.Day.CompareTo(b.Day);
        }

        /// <summary>The firm's trading day, same convention the tracker uses.</summary>
        public static DateTime DayOf(DateTime when, int resetMinute)
        {
            if (resetMinute < 0 || resetMinute >= 1440) resetMinute = 0;
            int minute = when.Hour * 60 + when.Minute;
            return resetMinute > 0 && minute < resetMinute ? when.Date.AddDays(-1) : when.Date;
        }

        /// <summary>
        /// Work out where the account stands. `today` is excluded from the
        /// banked figures and passed separately, because the ceiling is a
        /// statement about a day that is still open.
        /// </summary>
        public static PayoutStanding Stand(List<PayoutDay> days, DateTime today,
                                           double todayPnl, PayoutRules rules)
        {
            PayoutStanding s = new PayoutStanding();
            if (rules == null || !rules.Known) return s;
            s.Known = true;

            double banked = 0, best = 0;
            DateTime bestOn = DateTime.MinValue;
            int qualifying = 0, counted = 0;

            if (days != null)
            {
                for (int i = 0; i < days.Count; i++)
                {
                    PayoutDay d = days[i];
                    if (d == null) continue;
                    if (d.Day.Date == today.Date) continue;   // today is passed separately

                    counted++;
                    banked += d.Pnl;
                    if (d.Pnl > best) { best = d.Pnl; bestOn = d.Day; }
                    if (d.Pnl >= rules.QualifyingDayMinimum && d.Pnl > 0) qualifying++;
                }
            }

            // Today counts too, once it has happened.
            double net = banked + todayPnl;
            if (todayPnl > best) { best = todayPnl; bestOn = today.Date; }
            if (todayPnl >= rules.QualifyingDayMinimum && todayPnl > 0) qualifying++;
            if (todayPnl != 0) counted++;

            s.Days = counted;
            s.QualifyingDays = qualifying;
            s.DaysStillNeeded = rules.QualifyingDaysRequired > qualifying
                              ? rules.QualifyingDaysRequired - qualifying : 0;
            s.NetProfit = net;
            s.BestDay = best;
            s.BestDayOn = bestOn;
            s.Share = net > 0 && best > 0 ? best / net : 0;

            if (rules.HasConsistencyRule)
            {
                double r = rules.Fraction;

                // Exactly at the line clears. The two wordings disagree about
                // that - Apex's 4.0 page refuses at "50% or more", while its
                // legacy formula, Highest ÷ 0.3 = Minimum Total Profit
                // Required, requires exactly 30% to PASS. A tool that
                // contradicted the firm's own worked example would be wrong in
                // the way that costs trust, so the line clears here and the
                // ceiling below does the being-careful instead: it tells him to
                // stop SHORT of the line, not on it.
                s.Blocked = best > 0 && (net <= 0 || best > (net * r) + 0.005);

                if (s.Blocked && best > 0)
                {
                    double needed = (best / r) - net;
                    s.ProfitToUnblock = needed > 0 ? needed : 0;
                }

                // The ceiling is about today becoming the biggest day, so it is
                // built from the OTHER days only.
                if (banked > 0)
                {
                    double ceiling = (r * banked) / (1 - r);
                    s.CeilingToday = ceiling;
                    s.PastCeiling = todayPnl >= ceiling;
                }
                else
                {
                    // Nothing banked underneath today, so any winning day is
                    // the whole of the profit and no amount of stopping early
                    // helps. The honest thing to say is "you need more days",
                    // not "stop at zero".
                    s.CeilingToday = 0;
                    s.PastCeiling = false;
                }

                // Worth putting on screen once there is a payout to protect.
                // Before that, the true advice is "keep trading, you need days",
                // and a ceiling would be telling him to stop on a day where
                // stopping achieves nothing.
                s.CeilingWorthShowing = banked > 0
                                     && (net >= rules.MinimumPayout
                                         || qualifying >= rules.QualifyingDaysRequired);
            }

            s.CouldRequestNow = !s.Blocked
                             && qualifying >= rules.QualifyingDaysRequired
                             && net >= rules.MinimumPayout;
            return s;
        }
    }
}
