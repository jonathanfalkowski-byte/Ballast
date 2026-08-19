// ─────────────────────────────────────────────────────────────────────────────
// Ballast — SettingsCodec.cs
//
// Reading and writing ballast-settings.txt. This lived inside the window until
// the config grew to fifteen fields, at which point it became the most likely
// place for a silent, expensive bug: a settings file that half-loads leaves a
// trader with somebody else's drawdown figure and a cushion that lies to them.
//
// It is out here, with no NinjaTrader or WPF dependency, purely so it can be
// tested. Two properties matter and both are covered by tests:
//
//   1. Round-trip fidelity. What goes in comes back out, exactly.
//   2. Forward compatibility. A file written by an older build must still load,
//      with the fields it never knew about left at their defaults rather than
//      zeroed. An upgrade must never silently change somebody's position size.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Globalization;

namespace Ballast
{
    public static class SettingsCodec
    {
        /// <summary>
        /// Field count as of this build. Older files are shorter and that is fine;
        /// each block below checks the length it needs before reading.
        /// </summary>
        public const int CurrentFieldCount = 30;

        /// <summary>
        /// The field count that existed before the trading window, cooldown and
        /// firm contract cap were added. Kept as its own constant because the
        /// blocks below have to say which vintage of file they can read.
        /// </summary>
        private const int FieldsBeforeSession = 17;

        public static string Serialise(string key, TrackerConfig c)
        {
            if (c == null) c = new TrackerConfig();

            return string.Join("|", new string[] {
                Clean(key),                                                        // 0
                D(c.StartingBalance),                                              // 1
                D(c.TrailingDrawdown),                                             // 2
                ((int)c.DrawdownType).ToString(CultureInfo.InvariantCulture),      // 3
                I(c.MaxLossesBeforeStop),                                          // 4
                D(c.DailyLossLimit),                                               // 5
                D(c.DailyTarget),                                                  // 6
                I(c.MaxTrades),                                                    // 7
                I(c.MaxContracts),                                                 // 8
                D(c.LockFloorAt),                                                  // 9
                Clean(c.ProfileKey),                                               // 10
                D(c.RiskPctOfDrawdown),                                            // 11
                D(c.ThrottleStepPct),                                              // 12
                D(c.ThrottleCutPct),                                               // 13
                I(c.BaseMaxContracts),                                             // 14
                c.IsAutomated ? "1" : "0",                                         // 15
                ((int)c.Generation).ToString(CultureInfo.InvariantCulture),        // 16
                I(c.SessionStartMinute),                                           // 17
                I(c.SessionEndMinute),                                             // 18
                I(c.CooldownMinutes),                                              // 19
                I(c.FirmMaxContracts),                                             // 20
                D(c.ProfitTarget),                                                 // 21
                D(c.FirmDailyLossLimit),                                           // 22
                c.TrustAccountRealised ? "1" : "0",                                // 23
                I(c.TradingDayResetMinute),                                         // 24
                ((int)c.Purpose).ToString(CultureInfo.InvariantCulture),            // 25
                D(c.StopPerContract),                                               // 26
                c.LastPayoutOn == DateTime.MinValue.Date                            // 27
                    ? ""
                    : c.LastPayoutOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                I(c.PayoutsTaken),                                                  // 28
                D(c.FirmFloorLevel)                                                 // 29
            });
        }

        /// <summary>
        /// Parse one line. Returns null - never a half-built config - when the
        /// line is unusable, so a corrupt file drops the bad rows instead of
        /// loading garbage figures that would produce a wrong cushion.
        /// </summary>
        public static TrackerConfig Deserialise(string line, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(line)) return null;

            string[] f = line.Split('|');
            if (f.Length < 9) return null;

            TrackerConfig c = new TrackerConfig();
            double d; int n;

            if (double.TryParse(f[1], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.StartingBalance = d;
            if (double.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.TrailingDrawdown = d;
            if (int.TryParse(f[3], out n)) c.DrawdownType = n == 1 ? DrawdownType.EndOfDay : DrawdownType.Intraday;
            if (int.TryParse(f[4], out n)) c.MaxLossesBeforeStop = n;
            if (double.TryParse(f[5], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.DailyLossLimit = d;
            if (double.TryParse(f[6], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.DailyTarget = d;
            if (int.TryParse(f[7], out n)) c.MaxTrades = n;
            if (int.TryParse(f[8], out n)) c.MaxContracts = n;

            // Field 10 (lock level) arrived with the floor-lock work. Absent means
            // 0, which trails forever and understates the cushion - the safe way
            // to be wrong.
            if (f.Length >= 10 && double.TryParse(f[9], NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                c.LockFloorAt = d;

            // Fields 11-15 arrived with risk profiles. An older file stops short
            // and keeps the no-throttle defaults, so upgrading never silently
            // changes what size a trader is being advised to take.
            if (f.Length >= FieldsBeforeSession)
            {
                c.ProfileKey = f[10];
                if (double.TryParse(f[11], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.RiskPctOfDrawdown = d;
                if (double.TryParse(f[12], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.ThrottleStepPct = d;
                if (double.TryParse(f[13], NumberStyles.Any, CultureInfo.InvariantCulture, out d)) c.ThrottleCutPct = d;
                if (int.TryParse(f[14], out n)) c.BaseMaxContracts = n;
            }

            // Field 16 marks a bot-traded account. Older files predate the idea,
            // so everything they describe is treated as hand-traded - which is
            // what it was when they were written.
            if (f.Length >= 16) c.IsAutomated = f[15] == "1";

            // Field 17 records which generation this account belongs to. Older
            // files predate the distinction and stay on Auto, which picks the
            // tighter drawdown - the safe way to be wrong.
            if (f.Length >= 17 && int.TryParse(f[16], out n) && n >= 0 && n <= 2)
                c.Generation = (AccountGeneration)n;

            // Fields 18-21 arrived last, and until they did they were simply never
            // written down: a trader's session window, cooldown and the firm's own
            // contract cap all silently reverted to the built-in defaults on every
            // restart. Someone who trades the afternoon was told every day that
            // they were outside a 09:30-11:30 window they had never chosen.
            //
            // Absent still means "use the default", because that is all an older
            // file can tell us - but from this build on, what the trader set is
            // what comes back.
            if (f.Length >= 18 && int.TryParse(f[17], out n) && n >= 0 && n <= 1440) c.SessionStartMinute = n;
            if (f.Length >= 19 && int.TryParse(f[18], out n) && n >= 0 && n <= 1440) c.SessionEndMinute = n;
            if (f.Length >= 20 && int.TryParse(f[19], out n) && n >= 0 && n <= 720)  c.CooldownMinutes = n;
            if (f.Length >= 21 && int.TryParse(f[20], out n) && n >= 0)               c.FirmMaxContracts = n;
            if (f.Length >= 22 && double.TryParse(f[21], NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                c.ProfitTarget = d;

            // Field 23 separates the firm's published daily loss limit from the
            // trader's own. An older file has only the one number, and whatever
            // is in it is treated as the trader's - which is the safe reading,
            // because it is the one that keeps stopping them.
            if (f.Length >= 23 && double.TryParse(f[22], NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                c.FirmDailyLossLimit = d;

            // Field 24 decides whether the day's P&L is the account's own figure
            // or Ballast's measurement from its own baseline. Absent means ON:
            // an older file predates the choice, and agreeing with the platform
            // is the behaviour that cannot silently under-report a loss.
            if (f.Length >= 24) c.TrustAccountRealised = f[23] != "0";

            // Field 25 is the firm's trading-day boundary on NinjaTrader's
            // configured clock. Older files retain midnight, matching their
            // previous calendar-day behaviour.
            if (f.Length >= 25 && int.TryParse(f[24], out n) && n >= 0 && n < 1440)
                c.TradingDayResetMinute = n;

            // Field 26 is what the account is FOR - practice, evaluation, funded.
            // Absent means unsaid, and unsaid keeps an account out of any
            // comparison rather than putting it on the wrong side of one.
            if (f.Length >= 26 && int.TryParse(f[25], out n) && n >= 0 && n <= 3)
                c.Purpose = (AccountPurpose)n;

            // Field 27 is what a full stop costs on ONE contract on THIS account.
            // It used to be a single box that was never written down at all, so
            // every restart lost it and "use it on every account" put one
            // account's stop on all of them. Absent means unsaid, and unsaid is
            // exactly what it was before - no position size is worked out from
            // a figure nobody gave.
            if (f.Length >= 27 && double.TryParse(f[26], NumberStyles.Any, CultureInfo.InvariantCulture, out d)
                && d >= 0)
                c.StopPerContract = d;

            // Fields 28 and 29 are where the payout clock starts on this
            // account: the last approved payout, and how many have been taken.
            //
            // Consistency is measured from the last approved payout because the
            // firm measures it from there, and Ballast has no way to see a
            // withdrawal happen. Absent means "never paid out", which counts
            // the whole journal - the right reading for a file written before
            // this existed, and for an account that has genuinely never paid.
            if (f.Length >= 28 && !string.IsNullOrEmpty(f[27]) && f[27].Length == 8)
            {
                DateTime paid;
                if (DateTime.TryParseExact(f[27], "yyyyMMdd", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out paid))
                    c.LastPayoutOn = paid.Date;
            }
            if (f.Length >= 29 && int.TryParse(f[28], out n) && n >= 0)
                c.PayoutsTaken = n;

            // Field 30 is the firm's own liquidation threshold, typed off its
            // dashboard. Absent means Ballast falls back to its own estimate,
            // which is what every file before this one did.
            if (f.Length >= 30 && double.TryParse(f[29], NumberStyles.Any,
                                                  CultureInfo.InvariantCulture, out d) && d >= 0)
                c.FirmFloorLevel = d;

            // A throttle with no base size to count down from would cut against
            // the already-throttled number every tick, ratcheting size to 1.
            if (c.BaseMaxContracts <= 0) c.BaseMaxContracts = c.MaxContracts;

            key = f[0];
            return c;
        }

        private static string D(double v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        /// <summary>
        /// The separator must never appear inside a field. Account names come from
        /// the broker and a pipe in one would shift every later field along by one,
        /// quietly loading the drawdown into the wrong slot.
        /// </summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", "/");
        }
    }
}
