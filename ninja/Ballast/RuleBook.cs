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
    /// <summary>One firm-and-plan's payout terms, as read from the rule book.</summary>
    public class PayoutSpec
    {
        public string Firm;
        public string Plan;

        /// <summary>0 means every size on this plan.</summary>
        public double Size;

        public PayoutRules Rules = new PayoutRules();
    }

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

    /// <summary>
    /// What an account is for. Unsaid is the honest default: an account whose
    /// purpose nobody has stated is left out of any comparison rather than
    /// guessed at, because guessing is how a finding about a trader turns into a
    /// finding about a mislabelled account.
    /// </summary>
    public enum AccountPurpose { Unsaid, Practice, Evaluation, Funded }

    public class RuleBook
    {
        private readonly List<FirmAccountSpec> specs = new List<FirmAccountSpec>();

        // Payout terms, keyed loosely by firm and plan. Separate from specs
        // because most firms publish them separately, because they change on a
        // different schedule, and because a firm can perfectly well be in the
        // book with its drawdowns and without its payout terms - which must
        // read as "not published here", never as "no rule".
        private readonly List<PayoutSpec> payouts = new List<PayoutSpec>();

        public string VerifiedDate = "unknown";
        public string LoadError = null;
        public string SourcePath = null;
        public int Count { get { return specs.Count; } }

        /// <summary>
        /// When each firm's figures were last read off that firm's own pages, and
        /// where. Keyed by firm name.
        ///
        /// One date for the whole file was a quiet lie. It said "verified 4
        /// August" across a rule book in which two firms had been checked
        /// properly and seven had not, and a reader - on the public rules page,
        /// or in the window trusting a cushion - had no way to tell which kind of
        /// row he was looking at.
        ///
        /// A row nobody has confirmed is not a scandal. Presenting it as though
        /// somebody had is. So the file can now say, per firm, who checked and
        /// when, and everything without an entry reports itself as unconfirmed.
        ///
        /// LOST ONCE, on 10 August, and this is worth recording. A source file
        /// was edited on a copy of the repo that had silently rolled back, and
        /// the result was committed over the top - so these four members vanished
        /// from a build that still compiled locally, because the local copy of
        /// the test that used them had rolled back too. CI caught it and nothing
        /// else did.
        /// </summary>
        private readonly Dictionary<string, string> firmVerified =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> firmSource =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The date a firm's figures were last confirmed, or "" if never.</summary>
        public string VerifiedFor(string firm)
        {
            string v;
            if (firm != null && firmVerified.TryGetValue(firm, out v)) return v;
            return "";
        }

        /// <summary>Where a firm's figures were read from, or "" if unrecorded.</summary>
        public string SourceFor(string firm)
        {
            string v;
            if (firm != null && firmSource.TryGetValue(firm, out v)) return v;
            return "";
        }

        /// <summary>
        /// One line about how much weight a firm's numbers will carry. Shown
        /// wherever those numbers are, because a trader deciding whether to trust
        /// a cushion is entitled to know which kind of figure it rests on.
        /// </summary>
        public string ConfidenceFor(string firm)
        {
            string when = VerifiedFor(firm);
            if (when.Length == 0)
                return "Not independently confirmed. These figures came from the firm's public "
                     + "marketing rather than a page Ballast has checked - treat them as a "
                     + "starting point and verify against your own dashboard.";

            string src = SourceFor(firm);
            return "Read off " + (src.Length > 0 ? src : "the firm's own pages") + " on " + when
                 + ". Verify against your own dashboard before trusting a cushion.";
        }

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
        /// <summary>
        /// The payout terms for one plan, or an empty set meaning "this firm's
        /// payout terms are not in the book".
        ///
        /// A row with SIZE 0 covers every size on that plan; a row naming a
        /// size beats it, because Apex's qualifying-day minimum is per size and
        /// differs again between intraday and end-of-day.
        /// </summary>
        public PayoutRules PayoutFor(string firm, string plan, double size)
        {
            PayoutRules exact = null, any = null;
            for (int i = 0; i < payouts.Count; i++)
            {
                PayoutSpec p = payouts[i];
                if (p == null) continue;
                if (!string.Equals(p.Firm, firm, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(p.Plan, plan, StringComparison.OrdinalIgnoreCase)) continue;

                if (p.Size <= 0) any = p.Rules;
                else if (Math.Abs(p.Size - size) < 1) exact = p.Rules;
            }
            return exact != null ? exact : (any != null ? any : new PayoutRules());
        }

        /// <summary>
        /// The payout terms for a configured account, found the same way its
        /// drawdown figures are.
        /// </summary>
        public PayoutRules PayoutForAccount(string accountName, TrackerConfig c)
        {
            FirmAccountSpec s = MatchSpecForAccount(accountName, c);
            if (s == null) return new PayoutRules();
            return PayoutFor(s.Firm, s.Plan, s.Size);
        }

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
        /// Which account type do these saved figures describe? Null when the
        /// answer is not certain.
        ///
        /// "after i clicked on set its rules...it defaults to eval intraday
        /// 25k.....i think it should already populate that since we know what it
        /// is."
        ///
        /// He is right, and the reason it did not is that the CHOICE was never
        /// written down - only its consequences were. Ballast saved the size,
        /// the drawdown, the floor and the target, but not the name of the type
        /// he picked, so on the next start the dropdown had nothing to restore
        /// and fell to whatever happened to be first in the list. On a 250K
        /// account that is "Evaluation intraday - 25K", which is not a cosmetic
        /// problem: one careless Save and a 250K account is wearing a 25K
        /// account's floor.
        ///
        /// The fix is not to store the choice - a stored label can drift out of
        /// step with the figures, and then two records disagree and neither is
        /// obviously right. It is to READ the choice back out of the figures.
        /// Size, drawdown, whether the drawdown is intraday or end-of-day, and
        /// where the floor stops trailing identify a row in the rule book on
        /// their own. They cannot drift, because they ARE the account type.
        ///
        /// Only an unambiguous answer counts. If two rows fit, Ballast says
        /// nothing rather than picking one - a confident wrong type is worse
        /// than an empty dropdown, because it looks settled.
        /// </summary>
        public FirmAccountSpec MatchSpec(string firm, TrackerConfig c)
        {
            if (c == null || string.IsNullOrEmpty(firm)) return null;
            if (c.StartingBalance <= 0 || c.TrailingDrawdown <= 0) return null;

            List<FirmAccountSpec> all = ForFirm(firm);
            FirmAccountSpec found = null;

            for (int i = 0; i < all.Count; i++)
            {
                FirmAccountSpec s = all[i];
                if (Math.Abs(s.Size - c.StartingBalance) > 1) continue;
                if (Math.Abs(s.Drawdown - c.TrailingDrawdown) > 1) continue;
                if (s.DrawdownType != c.DrawdownType) continue;
                if (Math.Abs(s.LockFloorAt - c.LockFloorAt) > 1) continue;

                if (found != null) return null;      // two fit: say nothing
                found = s;
            }

            return found;
        }

        /// <summary>Same question, starting from the account's own name.</summary>
        public FirmAccountSpec MatchSpecForAccount(string accountName, TrackerConfig c)
        {
            return MatchSpec(FirmFromAccountName(accountName), c);
        }

        /// <summary>
        /// Fill in the figures that come from the account TYPE rather than from
        /// the trader, where they are missing. Returns whether anything changed.
        ///
        /// The profit target is the one that bites. It is what tells Ballast
        /// where an Apex evaluation's threshold stops trailing, and when it goes
        /// missing the account still knows its size and its floor - so the rules
        /// check can see that a floor locks at $265,000 with nothing to check it
        /// against, and says so every morning in red.
        ///
        /// That message asked the trader to "pick the account type again so
        /// Ballast can set it". But if Ballast is sure enough of the type to
        /// write that sentence, it is sure enough to set the figure itself.
        /// Asking a man to re-enter something you already know is not a warning,
        /// it is a chore.
        ///
        /// Only ever fills a BLANK. A figure the trader has entered is never
        /// touched, and neither is one that disagrees - a disagreement is a real
        /// warning and it still gets one.
        /// </summary>
        public bool FillFirmFigures(string accountName, TrackerConfig c)
        {
            FirmAccountSpec s = MatchSpecForAccount(accountName, c);
            if (s == null || c == null) return false;

            bool changed = false;

            if (c.ProfitTarget <= 0 && s.ProfitTarget > 0)
            { c.ProfitTarget = s.ProfitTarget; changed = true; }

            if (c.FirmMaxContracts <= 0 && s.FirmMaxContracts > 0)
            { c.FirmMaxContracts = s.FirmMaxContracts; changed = true; }

            if (c.FirmDailyLossLimit <= 0 && s.DailyLossLimit > 0)
            { c.FirmDailyLossLimit = s.DailyLossLimit; changed = true; }

            return changed;
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
            return AutoDetect(accountName, balance, preferIntraday, generation, "");
        }

        /// <summary>
        /// As above, but preferring rows written for a particular platform.
        ///
        /// When the platform is unknown the SAFE row wins, not the first one:
        /// among otherwise equal candidates the one that keeps trailing is
        /// chosen, because a floor that keeps following the peak reports less
        /// room than one that stops. Being cautiously wrong about a threshold is
        /// survivable; the opposite is how an account dies at a number the trader
        /// thought was still safe.
        /// </summary>
        public FirmAccountSpec AutoDetect(string accountName, double balance, bool preferIntraday,
                                          AccountGeneration generation, string platform)
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

            best = PreferPlatform(firm, best, platform);

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
        /// <summary>
        /// Which trading platform a rule row is written for, or "" when the row
        /// applies to all of them.
        ///
        /// This exists because Apex's intraday EVALUATION threshold behaves
        /// differently depending on how you connect. Their help centre is
        /// explicit: on Rithmic and WealthCharts the threshold "stops trailing
        /// and becomes fixed when it reaches an amount equal to the Target Profit
        /// balance", while on Tradovate it "trails indefinitely with the peak
        /// account balance". Same firm, same account size, same evaluation - a
        /// materially different floor.
        ///
        /// No prop-firm comparison site captures this. It only shows up if you
        /// are writing software that has to produce a number a trader will act
        /// on, which is the whole argument for keeping this rule book honest.
        /// </summary>
        public static string PlatformOfPlan(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return "";
            string p = plan.ToUpperInvariant();

            if (p.IndexOf("TRADOVATE", StringComparison.Ordinal) >= 0) return "TRADOVATE";
            if (p.IndexOf("RITHMIC", StringComparison.Ordinal) >= 0) return "RITHMIC";
            if (p.IndexOf("WEALTHCHART", StringComparison.Ordinal) >= 0) return "RITHMIC";
            return "";
        }

        /// <summary>
        /// Normalise whatever NinjaTrader calls a connection into the platform
        /// names the rule book uses. Returns "" when it is not one we know, which
        /// means "do not use platform to decide anything".
        /// </summary>
        /// <summary>
        /// The names NinjaTrader gives its own built-in simulated accounts:
        /// Sim101, Playback101, Backtest.
        ///
        /// Used as the last resort when an account's provider cannot be read, to
        /// decide whether it may be offered a one-click "start its day over" on
        /// the Now page. That button un-spends a day, so the test has to be exact:
        /// the stem must BE one of these words, with only a short run of digits
        /// after it. A substring match would hand the button to "SimplyFunded" or
        /// an account somebody named "Sim - live money", and next to a funded
        /// account one wrong click costs a real day.
        /// </summary>
        public static bool IsBuiltInSimName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string stem = name.Trim();
            int digits = 0;
            while (stem.Length > 0 && stem[stem.Length - 1] >= '0' && stem[stem.Length - 1] <= '9')
            {
                stem = stem.Substring(0, stem.Length - 1);
                digits++;
            }
            if (digits > 4) return false;

            return string.Equals(stem, "Sim", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, "Playback", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, "Backtest", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Is this NinjaTrader's Market Replay account?
        ///
        /// Practice is not sim. A sim account runs on the real clock, on real
        /// incoming prices, and cannot be rewound - a bad morning on Sim103 is a
        /// bad morning that happened. A replay is a recording: the clock is the
        /// recorded one, the fills are modelled against bars that already
        /// printed, and the whole session can be run again until it goes well.
        ///
        /// Everything Ballast measures about real trading has to stay out of
        /// reach of that, which is why this is its own question rather than a
        /// shade of IsBuiltInSimName.
        /// </summary>
        public static bool IsPracticeAccountName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string stem = name.Trim();
            int digits = 0;
            while (stem.Length > 0 && stem[stem.Length - 1] >= '0' && stem[stem.Length - 1] <= '9')
            {
                stem = stem.Substring(0, stem.Length - 1);
                digits++;
            }
            if (digits > 4) return false;

            return string.Equals(stem, "Playback", StringComparison.OrdinalIgnoreCase);
        }

        public static string PlatformFromConnection(string connectionName)
        {
            if (string.IsNullOrEmpty(connectionName)) return "";
            string c = connectionName.ToUpperInvariant();

            if (c.IndexOf("TRADOVATE", StringComparison.Ordinal) >= 0) return "TRADOVATE";
            if (c.IndexOf("RITHMIC", StringComparison.Ordinal) >= 0) return "RITHMIC";
            if (c.IndexOf("WEALTHCHART", StringComparison.Ordinal) >= 0) return "RITHMIC";
            return "";
        }

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

        /// <summary>
        /// Does this account's configuration contradict what its own name says
        /// it is? Returns "" when nothing looks wrong.
        ///
        /// This exists because the failure is silent and expensive. An account
        /// named APEX-11325-109 is an Apex EVALUATION - no PA prefix - and an
        /// Apex evaluation threshold never stops trailing. Configure it with a
        /// floor that locks and Ballast will happily report thousands of dollars
        /// of room that does not exist, right up until the account is closed.
        ///
        /// Every check here only ever fires when the settings are more generous
        /// than the firm's published rules. Being told you have LESS room than
        /// you really do is survivable; the opposite is how accounts die, and it
        /// is the one thing this tool must never do quietly.
        /// </summary>
        public string SanityWarning(string accountName, TrackerConfig c)
        {
            return SanityWarning(accountName, c, "");
        }

        public string SanityWarning(string accountName, TrackerConfig c, string platform)
        {
            if (c == null || string.IsNullOrEmpty(accountName)) return "";

            string firm = FirmFromAccountName(accountName);
            if (string.IsNullOrEmpty(firm)) return "";        // sim, or a name that says nothing

            bool funded = IsFundedAccountName(accountName);

            // 1. An evaluation whose floor stops trailing.
            //
            // Whether that is right depends on the platform, which is why this
            // check needs to know. On Apex over Tradovate the threshold never
            // stops; over Rithmic or WealthCharts it fixes at the target profit
            // balance. Both are the firm's own published rules, and picking the
            // wrong one moves the floor by thousands.
            if (!funded && c.LockFloorAt > 0)
            {
                if (platform == "TRADOVATE")
                {
                    return "this is a " + firm + " evaluation on Tradovate, where the threshold "
                         + "trails the peak indefinitely - but it is set to stop at "
                         + Money(c.LockFloorAt) + ". Set \"stops trailing at\" to 0, or your room "
                         + "is being overstated.";
                }

                // Rithmic, WealthCharts, or unknown: a lock is legitimate, but it
                // belongs at the target profit balance and nowhere else.
                if (c.ProfitTarget > 0)
                {
                    double expect = c.StartingBalance + c.ProfitTarget;
                    if (Math.Abs(c.LockFloorAt - expect) > 1)
                    {
                        return "the threshold on this evaluation is set to stop at "
                             + Money(c.LockFloorAt) + ", but it should stop at the target profit "
                             + "balance - " + Money(c.StartingBalance) + " plus "
                             + Money(c.ProfitTarget) + " is " + Money(expect) + ".";
                    }
                }
                else if (platform.Length == 0)
                {
                    return "this looks like a " + firm + " evaluation with a floor set to stop "
                         + "trailing at " + Money(c.LockFloorAt) + ", and there is no profit "
                         + "target recorded to check that against. Pick the account type again so "
                         + "Ballast can set it, or your room may be overstated.";
                }
            }

            // 2. A drawdown larger than anything the firm publishes at this size.
            List<FirmAccountSpec> all = ForFirm(firm);
            double biggest = 0;
            bool sizeKnown = false;

            for (int i = 0; i < all.Count; i++)
            {
                if (Math.Abs(all[i].Size - c.StartingBalance) > 1) continue;
                sizeKnown = true;
                if (all[i].Drawdown > biggest) biggest = all[i].Drawdown;
            }

            if (sizeKnown && biggest > 0 && c.TrailingDrawdown > biggest + 1)
            {
                return "a " + firm + " account of " + Money(c.StartingBalance) + " has at most "
                     + Money(biggest) + " of drawdown, but this one is set to "
                     + Money(c.TrailingDrawdown) + " - that is more room than the firm gives you.";
            }

            return "";
        }

        private static string Money(double n)
        {
            double r = Math.Round(n);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Swap a chosen row for the equivalent row written for this platform.
        /// With no platform known, take whichever equivalent row trails longest.
        /// </summary>
        private FirmAccountSpec PreferPlatform(string firm, FirmAccountSpec best, string platform)
        {
            if (best == null) return null;

            List<FirmAccountSpec> all = ForFirm(firm);
            FirmAccountSpec safest = best;

            for (int i = 0; i < all.Count; i++)
            {
                FirmAccountSpec o = all[i];

                // Same account in every respect except which platform it is for.
                if (Math.Abs(o.Size - best.Size) > 1) continue;
                if (Math.Abs(o.Drawdown - best.Drawdown) > 1) continue;
                if (o.DrawdownType != best.DrawdownType) continue;
                if (IsFundedPlanName(o.Plan) != IsFundedPlanName(best.Plan)) continue;
                if (IsLegacyPlanName(o.Plan) != IsLegacyPlanName(best.Plan)) continue;

                string rowPlatform = PlatformOfPlan(o.Plan);

                if (platform.Length > 0 && rowPlatform == platform) return o;

                // No platform to go on: prefer the row that never stops trailing,
                // and otherwise the one that stops latest.
                if (platform.Length == 0)
                {
                    if (o.LockFloorAt <= 0) safest = o;
                    else if (safest.LockFloorAt > 0 && o.LockFloorAt > safest.LockFloorAt) safest = o;
                }
            }

            return platform.Length > 0 ? best : safest;
        }

        public static TrackerConfig ToConfig(FirmAccountSpec s, TrackerConfig keepPersonalFrom)
        {
            TrackerConfig c = keepPersonalFrom != null
                ? BallastMonitor.CloneConfig(keepPersonalFrom)
                : new TrackerConfig();

            c.StartingBalance  = s.Size;
            c.TrailingDrawdown = s.Drawdown;
            c.DrawdownType     = s.DrawdownType;
            c.LockFloorAt      = s.LockFloorAt;      // 0 == assume it trails forever

            // The daily loss limit is TWO numbers wearing one name, and treating
            // them as one was quietly disarming accounts.
            //
            // "How much am I willing to lose today" is the trader's own decision
            // and the single most-used setting in Ballast. The firm's published
            // daily limit - where a firm publishes one at all - is a hard rule
            // that breaches the account. Picking an account type used to write
            // the second straight over the first, so a trader who set $500 and
            // then told Ballast the account was an Apex 250K (Apex publishes no
            // daily limit) got a silent 0: no daily stop, no warning, nothing.
            //
            // Now the firm's figure is recorded on its own, the trader's is left
            // exactly where they put it, and the tighter of the two binds -
            // because the firm's is a ceiling, never a suggestion.
            c.FirmDailyLossLimit = s.DailyLossLimit;
            if (s.DailyLossLimit > 0)
            {
                if (c.DailyLossLimit <= 0 || c.DailyLossLimit > s.DailyLossLimit)
                    c.DailyLossLimit = s.DailyLossLimit;
            }
            // The firm's number is what it takes to PASS, over however many days
            // that takes. It is recorded, and shown, but it is never allowed to
            // become the trader's target for one session - $15,000 as a daily
            // target on a 250K evaluation means the account is never once told to
            // bank a good day.
            c.ProfitTarget = s.ProfitTarget;

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
            payouts.Clear();
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
                        // VERIFIED|date                 - the file as a whole
                        // VERIFIED|firm|date|source     - one firm, checked properly
                        if (f.Length >= 3)
                        {
                            string vfirm = f[1].Trim();
                            firmVerified[vfirm] = f[2].Trim();
                            if (f.Length >= 4) firmSource[vfirm] = f[3].Trim();
                        }
                        else
                        {
                            VerifiedDate = f[1].Trim();
                        }
                        continue;
                    }

                    // PAYOUT|firm|plan|size|consistency%|day minimum|days|
                    //        minimum payout|payouts before the terms change
                    if (f.Length >= 9 && f[0].Trim().ToUpperInvariant() == "PAYOUT")
                    {
                        PayoutSpec ps = new PayoutSpec();
                        ps.Firm = f[1].Trim();
                        ps.Plan = f[2].Trim();

                        double pv; int pi;
                        double.TryParse(f[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pv);
                        ps.Size = pv;
                        double.TryParse(f[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pv);
                        ps.Rules.ConsistencyPct = pv;
                        double.TryParse(f[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pv);
                        ps.Rules.QualifyingDayMinimum = pv;
                        if (int.TryParse(f[6].Trim(), out pi)) ps.Rules.QualifyingDaysRequired = pi;
                        double.TryParse(f[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pv);
                        ps.Rules.MinimumPayout = pv;
                        if (int.TryParse(f[8].Trim(), out pi)) ps.Rules.MaxPayouts = pi;

                        if (!string.IsNullOrEmpty(ps.Firm) && ps.Rules.Known) payouts.Add(ps);
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
