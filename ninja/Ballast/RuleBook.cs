// ─────────────────────────────────────────────────────────────────────────────
// Ballast — RuleBook.cs
//
// Loads prop firm account specifications from ballast-rules.txt so the figures
// can be corrected WITHOUT recompiling. Prop firms change rules constantly; a
// rule set baked into a DLL goes stale and quietly produces wrong cushions.
//
// If the file is missing or unreadable the rule book is simply empty and the
// trader enters figures by hand. It never invents values.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Ballast
{
    public class FirmAccountSpec
    {
        public string Firm;
        public string Plan;
        public double Size;
        public double Drawdown;
        public DrawdownType DrawdownType;
        public double DailyLossLimit;
        public double ProfitTarget;
        public string Note;
        /// <summary>Level where the floor stops trailing. 0 = trails forever (safe default).</summary>
        public double LockFloorAt;

        /// <summary>
        /// The firm's own hard contract cap for this account size. 0 = unknown.
        /// Ballast never advises above it: a risk calculation that suggests more
        /// contracts than the firm allows is not a position, it is a violation.
        /// </summary>
        public int FirmMaxContracts;

        /// <summary>Label shown in the account-type dropdown.</summary>
        public string Label
        {
            get { return Plan + " - " + SizeLabel(Size); }
        }

        public static string SizeLabel(double size)
        {
            if (size >= 1000) return (size / 1000).ToString("0", CultureInfo.InvariantCulture) + "K";
            return size.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Which generation of a firm's accounts the trader holds.
    ///
    /// Sizes that exist in both (an Apex 25K / 50K / 100K / 150K) carry DIFFERENT
    /// drawdowns - a legacy 50K trails $2,500, a 4.0 50K trails $2,000 - and a
    /// balance cannot tell them apart. Guessing per account is fine for someone
    /// with one; it is twenty wrong guesses for someone whose whole book is
    /// legacy, so it is stated once and applied everywhere.
    /// </summary>
    public enum AccountGeneration { Auto, Legacy, Current }

    public class RuleBook
    {
        private readonly List<FirmAccountSpec> specs = new List<FirmAccountSpec>();

        public string VerifiedDate = "unknown";
        public string LoadError = null;
        public string SourcePath = null;
        public int Count { get { return specs.Count; } }

        /// <summary>Distinct firm names, in file order.</summary>
        public List<string> Firms()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < specs.Count; i++)
            {
                if (!list.Contains(specs[i].Firm)) list.Add(specs[i].Firm);
            }
            return list;
        }

        /// <summary>All account types for a firm, in file order.</summary>
        public List<FirmAccountSpec> ForFirm(string firm)
        {
            List<FirmAccountSpec> list = new List<FirmAccountSpec>();
            if (string.IsNullOrEmpty(firm)) return list;

            for (int i = 0; i < specs.Count; i++)
            {
                if (string.Equals(specs[i].Firm, firm, StringComparison.OrdinalIgnoreCase))
                    list.Add(specs[i]);
            }
            return list;
        }

        /// <summary>
        /// Best match for a firm given an observed balance. Used to auto-pick the
        /// account type when the trader has many accounts of different sizes.
        /// Returns null when no size is close enough — it does not guess.
        /// </summary>
        public FirmAccountSpec MatchByBalance(string firm, double balance, string preferredPlan)
        {
            List<FirmAccountSpec> candidates = ForFirm(firm);
            if (candidates.Count == 0 || balance <= 0) return null;

            FirmAccountSpec best = null;
            double bestDiff = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                FirmAccountSpec s = candidates[i];

                // If a plan is preferred, only consider that plan.
                if (!string.IsNullOrEmpty(preferredPlan) &&
                    !string.Equals(s.Plan, preferredPlan, StringComparison.OrdinalIgnoreCase))
                    continue;

                double diff = Math.Abs(balance - s.Size);
                if (diff < bestDiff) { bestDiff = diff; best = s; }
            }

            if (best == null) return null;

            // Only accept a genuinely close match (12% slack for drawdown/profit).
            if (bestDiff > best.Size * 0.12) return null;
            return best;
        }

        /// <summary>
        /// Guess the firm from the account's own name. Brokers name funded
        /// accounts after the firm - "APEX-11325-101", "PA-APEX-11325-04" - so the
        /// trader should not have to tell Ballast something it can already read.
        ///
        /// Returns "" rather than guessing when the name says nothing. Sim,
        /// Playback and Backtest accounts are explicitly refused: they carry no
        /// firm, and silently configuring one as an Apex account would put a
        /// wrong drawdown on a practice account and teach the wrong habits.
        /// </summary>
        public string FirmFromAccountName(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return "";

            string n = accountName.ToUpperInvariant();

            if (n.StartsWith("SIM") || n.StartsWith("PLAYBACK") || n.StartsWith("BACKTEST"))
                return "";

            // Token -> firm. Checked against firms actually present in the rule
            // book, so a rule-book edit that removes a firm cannot leave this
            // pointing at nothing.
            string[][] hints = new string[][]
            {
                new string[] { "APEX",        "Apex Trader Funding" },
                new string[] { "TOPSTEP",     "Topstep" },
                new string[] { "TST",         "Topstep" },
                new string[] { "TAKEPROFIT",  "Take Profit Trader" },
                new string[] { "TPT",         "Take Profit Trader" },
                new string[] { "MYFUNDED",    "MyFundedFutures" },
                new string[] { "MFF",         "MyFundedFutures" }
            };

            for (int i = 0; i < hints.Length; i++)
            {
                if (n.IndexOf(hints[i][0], StringComparison.Ordinal) < 0) continue;
                if (ForFirm(hints[i][1]).Count > 0) return hints[i][1];
            }

            return "";
        }

        /// <summary>A funded account, by broker naming convention. Apex uses "PA-".</summary>
        public static bool IsFundedAccountName(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return false;
            string n = accountName.ToUpperInvariant();
            return n.StartsWith("PA-") || n.StartsWith("PA ") || n.IndexOf("-PA-", StringComparison.Ordinal) >= 0;
        }

        public static bool IsFundedPlanName(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return false;
            string p = plan.ToUpperInvariant();
            return p.IndexOf("PA", StringComparison.Ordinal) >= 0
                || p.IndexOf("FUNDED", StringComparison.Ordinal) >= 0
                || p.IndexOf("LIVE", StringComparison.Ordinal) >= 0;
        }

        public static bool IsEvalPlanName(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return false;
            string p = plan.ToUpperInvariant();
            return p.IndexOf("EVALUATION", StringComparison.Ordinal) >= 0
                || p.IndexOf("EVAL", StringComparison.Ordinal) >= 0
                || p.IndexOf("COMBINE", StringComparison.Ordinal) >= 0
                || p.IndexOf("TEST", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Everything at once: read the firm off the name, then pick the account
        /// size from the balance. Returns null when either step is unsure.
        /// </summary>
        public FirmAccountSpec AutoDetect(string accountName, double balance, bool preferIntraday)
        {
            return AutoDetect(accountName, balance, preferIntraday, AccountGeneration.Auto);
        }

        public FirmAccountSpec AutoDetect(string accountName, double balance, bool preferIntraday,
                                          AccountGeneration generation)
        {
            string firm = FirmFromAccountName(accountName);
            if (firm.Length == 0 || balance <= 0) return null;

            // Evaluation or funded? Apex prefixes funded accounts "PA-", and the
            // two have DIFFERENT floor-lock behaviour - guessing wrong overstates
            // an evaluation's cushion by thousands.
            bool funded = IsFundedAccountName(accountName);

            // Preference order, most specific first. Each pass narrows on a real
            // distinction; nothing here ever falls back to "any row at all",
            // which is how a funded account previously picked up an evaluation's
            // rules just because they shared a size.
            FirmAccountSpec best = null;

            best = PickClosest(firm, balance, preferIntraday, funded, true, true, generation);
            if (best == null) best = PickClosest(firm, balance, preferIntraday, funded, true, false, generation);
            if (best == null) best = PickClosest(firm, balance, preferIntraday, funded, false, true, generation);
            if (best == null) best = PickClosest(firm, balance, preferIntraday, funded, false, false, generation);

            // Only now relax the generation, so a stated preference is honoured
            // wherever it can be, and ignored only when that size does not exist
            // in the requested generation at all.
            if (best == null && generation != AccountGeneration.Auto)
                best = AutoDetect(accountName, balance, preferIntraday, AccountGeneration.Auto);

            if (best == null) return null;

            // Where the generation was stated, it has already been honoured and
            // there is nothing left to disambiguate.
            if (generation != AccountGeneration.Auto) return best;

            // Otherwise a size may exist in both generations, and balance cannot
            // tell them apart - so take the TIGHTER drawdown. That puts the floor
            // higher and reports LESS cushion than the trader may really have.
            // Cautiously wrong is survivable here; the opposite is not.
            List<FirmAccountSpec> all = ForFirm(firm);
            for (int i = 0; i < all.Count; i++)
            {
                FirmAccountSpec o = all[i];
                if (o.Size != best.Size) continue;
                if (o.DrawdownType != best.DrawdownType) continue;
                if (IsFundedPlanName(o.Plan) != IsFundedPlanName(best.Plan)) continue;
                if (o.Drawdown > 0 && o.Drawdown < best.Drawdown) best = o;
            }

            return best;
        }

        /// <summary>
        /// Closest size match within a filtered slice of the rule book. Returns
        /// null rather than a distant guess - a 12% window, same as MatchByBalance.
        /// </summary>
        public static bool IsLegacyPlanName(string plan)
        {
            return !string.IsNullOrEmpty(plan)
                && plan.ToUpperInvariant().IndexOf("LEGACY", StringComparison.Ordinal) >= 0;
        }

        private FirmAccountSpec PickClosest(string firm, double balance, bool preferIntraday,
                                            bool funded, bool matchType, bool matchFunded,
                                            AccountGeneration generation)
        {
            List<FirmAccountSpec> all = ForFirm(firm);
            FirmAccountSpec best = null;
            double bestDiff = double.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                FirmAccountSpec s = all[i];

                if (matchType && (s.DrawdownType == DrawdownType.Intraday) != preferIntraday) continue;

                if (generation == AccountGeneration.Legacy && !IsLegacyPlanName(s.Plan)) continue;
                if (generation == AccountGeneration.Current && IsLegacyPlanName(s.Plan)) continue;

                if (matchFunded)
                {
                    bool isFunded = IsFundedPlanName(s.Plan);
                    bool isEval = IsEvalPlanName(s.Plan);
                    // Only enforce it where the rule book actually draws the line.
                    if ((isFunded || isEval) && isFunded != funded) continue;
                }

                double diff = Math.Abs(balance - s.Size);
                if (diff < bestDiff) { bestDiff = diff; best = s; }
            }

            if (best == null) return null;
            if (bestDiff > best.Size * 0.12) return null;
            return best;
        }

        public static TrackerConfig ToConfig(FirmAccountSpec s, TrackerConfig keepPersonalFrom)
        {
            TrackerConfig c = keepPersonalFrom != null
                ? BallastMonitor.CloneConfig(keepPersonalFrom)
                : new TrackerConfig();

            c.StartingBalance  = s.Size;
            c.TrailingDrawdown = s.Drawdown;
            c.DrawdownType     = s.DrawdownType;
            c.DailyLossLimit   = s.DailyLossLimit;   // 0 == firm publishes none
            c.LockFloorAt      = s.LockFloorAt;      // 0 == assume it trails forever
            if (s.ProfitTarget > 0) c.DailyTarget = s.ProfitTarget;

            // The firm's cap is a ceiling, never a suggestion. If the trader's own
            // number is smaller, theirs wins - this only ever brings size down.
            c.FirmMaxContracts = s.FirmMaxContracts;
            if (s.FirmMaxContracts > 0)
            {
                if (c.MaxContracts > s.FirmMaxContracts) c.MaxContracts = s.FirmMaxContracts;
                if (c.BaseMaxContracts > s.FirmMaxContracts) c.BaseMaxContracts = s.FirmMaxContracts;
            }

            return c;
        }

        public bool Load(string path)
        {
            specs.Clear();
            LoadError = null;
            SourcePath = path;

            try
            {
                if (!File.Exists(path))
                {
                    LoadError = "Rule book not found at " + path;
                    return false;
                }

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = (lines[i] ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    string[] f = line.Split('|');

                    if (f.Length >= 2 && f[0].Trim().ToUpperInvariant() == "VERIFIED")
                    {
                        VerifiedDate = f[1].Trim();
                        continue;
                    }

                    if (f.Length < 7) continue;

                    FirmAccountSpec s = new FirmAccountSpec();
                    s.Firm = f[0].Trim();
                    s.Plan = f[1].Trim();

                    double d;
                    if (!double.TryParse(f[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) continue;
                    s.Size = d;

                    if (!double.TryParse(f[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) continue;
                    s.Drawdown = d;

                    s.DrawdownType = f[4].Trim().ToUpperInvariant() == "INTRADAY"
                        ? DrawdownType.Intraday : DrawdownType.EndOfDay;

                    if (double.TryParse(f[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                        s.DailyLossLimit = d;

                    if (double.TryParse(f[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                        s.ProfitTarget = d;

                    s.Note = f.Length >= 8 ? f[7].Trim() : "";

                    // Optional 9th field: the level at which trailing stops.
                    // Absent => 0 => we assume it trails forever, which UNDERSTATES
                    // the cushion. Deliberately the conservative direction to be wrong in.
                    if (f.Length >= 9 && double.TryParse(f[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                        s.LockFloorAt = d;

                    // Optional 10th field: the firm's own contract cap.
                    int mc;
                    if (f.Length >= 10 && int.TryParse(f[9].Trim(), out mc) && mc > 0)
                        s.FirmMaxContracts = mc;

                    if (s.Size > 0 && s.Drawdown > 0) specs.Add(s);
                }

                if (specs.Count == 0) LoadError = "Rule book parsed but contained no valid account lines.";
                return specs.Count > 0;
            }
            catch (Exception ex)
            {
                LoadError = "Could not read rule book: " + ex.Message;
                return false;
            }
        }
    }
}
