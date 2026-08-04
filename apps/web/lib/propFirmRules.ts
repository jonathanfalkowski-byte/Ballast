// ─────────────────────────────────────────────────────────────────────────────
// propFirmRules.ts — the canonical prop firm rule book.
//
// This is the single source of truth, served to the Ballast NinjaTrader add-on
// (and anything else) from /api/rules. The add-on fetches it on a schedule, so
// correcting a figure here reaches every user without them doing anything.
//
// Prop firms change rules often. When a change is spotted:
//   1. update the affected lines,
//   2. bump RULES_VERSION,
//   3. update RULES_VERIFIED,
//   4. deploy.
// Clients pick it up automatically on their next check.
//
// Format is deliberately plain text, not JSON: the add-on parses it in C#
// without needing a JSON library, using the same tested parser.
//
//   FIRM | PLAN | SIZE | DRAWDOWN | DDTYPE | DAILYLOSS | TARGET | NOTE | LOCKAT
//
//   DDTYPE    = INTRADAY or EOD
//   DAILYLOSS = 0 means the firm publishes no separate daily loss limit
//   LOCKAT    = balance at which the trailing floor FREEZES and stops following
//               you up. Optional 9th field. 0 (or absent) means "assume it
//               trails forever", which understates the trader's cushion. That
//               is the safe direction to be wrong in, so never guess a value
//               here — leave it 0 until the firm's own page confirms it.
// ─────────────────────────────────────────────────────────────────────────────

/** Bump on every rule change. Clients compare this to decide whether to update. */
export const RULES_VERSION = 12;

/** Date the figures below were last checked against the firms' own pages. */
export const RULES_VERIFIED = "2026-08-04";

/** Pages a maintenance job should re-check for changes. */
export const RULE_SOURCES: Array<{ firm: string; url: string }> = [
  { firm: "Apex Trader Funding", url: "https://apextraderfunding.com/help-center/" },
  { firm: "Apex Trader Funding", url: "https://apextraderfunding.com/help-center/intraday-trailing-drawdown-accounts/intraday-trailing-drawdown-explained/" },
  { firm: "Apex Trader Funding", url: "https://apextraderfunding.com/help-center/evaluation-accounts-ea/legacy-evaluation-rules/" },
  { firm: "Topstep", url: "https://www.topstep.com/express-funded-account-rules" },
  { firm: "Topstep", url: "https://help.topstep.com/en/articles/8284204-what-is-the-maximum-loss-limit" },
  { firm: "Take Profit Trader", url: "https://takeprofittrader.com/" },
  { firm: "MyFundedFutures", url: "https://myfundedfutures.com/" },
  { firm: "Bulenox", url: "https://bulenox.com/help/qualification-account/" },
  { firm: "Tradeify", url: "https://help.tradeify.co/en/articles/10495897-rules-trailing-max-drawdowns" },
  { firm: "TradeDay", url: "https://tradeday.freshdesk.com/en/support/solutions/articles/103000008855-what-is-the-maximum-drawdown-rule-" },
  { firm: "Earn2Trade", url: "https://help.earn2trade.com/en/articles/5372687-how-does-end-of-day-drawdown-work" },
  { firm: "Legends Trading", url: "https://thelegendstrading.com/plans" },
  { firm: "Elite Trader Funding", url: "https://elitetraderfunding.app/evaluations" },
];

export const RULES_TEXT = `# Ballast rule book - served from tradeballast.com/api/rules
# Figures are a convenience, not gospel. Always verify against your own firm.
# A wrong drawdown produces a wrong cushion, which is worse than no tool at all.

VERSION|${RULES_VERSION}
VERIFIED|${RULES_VERIFIED}

# ─────────────────────────────────────────────────────────────────────────────
# Ballast rule book
#
# Prop firm rules change often. This file is plain text ON PURPOSE so you can
# correct it yourself the moment a firm changes something, without recompiling.
# Edit it, then click "Reload rule book" in the Ballast window.
#
# THESE FIGURES ARE A CONVENIENCE, NOT GOSPEL. Always verify against your own
# firm's dashboard before trusting the cushion number. A wrong drawdown here
# produces a wrong cushion, which is worse than no tool at all.
#
# Format (pipe separated, one account type per line):
#   FIRM | PLAN | SIZE | DRAWDOWN | DDTYPE | DAILYLOSS | TARGET | NOTE | LOCKAT | MAXCONTRACTS
#
#   DDTYPE     = INTRADAY or EOD
#   DAILYLOSS  = 0 means the firm publishes no separate daily loss limit
#   NOTE       = free text, shown in the window
#
# Lines starting with # are ignored.
# ─────────────────────────────────────────────────────────────────────────────


# -- Apex Trader Funding (4.0, effective 1 March 2026) ------------------------
# EVALUATIONS AND FUNDED (PA) ACCOUNTS DIFFER, AND SO DO PLATFORMS.
#
# Apex publishes THREE behaviours for the intraday threshold:
#   Performance (funded)          - stops once it reaches Starting Balance + $100
#   Evaluation, Rithmic/WealthCharts - stops at the Target Profit balance
#   Evaluation, Tradovate         - never stops, trails the peak forever
#
# All three are below as separate rows. Ballast reads the account's connection
# to pick the right one; where it cannot tell, it takes the row that keeps
# trailing, which reports LESS room rather than more. That is the direction to
# be wrong in - assuming a lock that is not there would tell a trader they have
# thousands more room than they do.
#
# 4.0 accounts only go up to 150K. If you hold a 75K, 250K or 300K it is a
# LEGACY account - see the legacy section below, whose drawdowns come from Apex's
# own Legacy Evaluation Rules page.
# Apex intraday EVALUATIONS behave differently depending on the platform, which
# is Apex's own rule, not a guess. From their help centre:
#   Rithmic and WealthCharts - "Intraday Threshold stops trailing and becomes
#     fixed when it reaches an amount equal to the Target Profit balance."
#     On a 50K with a 3,000 target that is a threshold fixed at 53,000, reached
#     once the highest balance touches 55,000.
#   Tradovate - "Intraday Drawdown trails indefinitely with the peak account
#     balance" and never stops.
# So LOCKAT below is (size + profit target) on Rithmic/WealthCharts and 0 on
# Tradovate. NinjaTrader normally reaches Apex over Rithmic.
Apex Trader Funding|Evaluation intraday (Rithmic/WealthCharts)|25000|1000|INTRADAY|0|1500|4.0. Threshold fixes at the target profit balance, reached when your peak balance is target balance + drawdown. No daily loss limit on intraday evals.|26500|4
Apex Trader Funding|Evaluation intraday (Rithmic/WealthCharts)|50000|2000|INTRADAY|0|3000|4.0. Threshold fixes at the target profit balance, reached when your peak balance is target balance + drawdown. No daily loss limit on intraday evals.|53000|6
Apex Trader Funding|Evaluation intraday (Rithmic/WealthCharts)|100000|3000|INTRADAY|0|6000|4.0. Threshold fixes at the target profit balance, reached when your peak balance is target balance + drawdown. No daily loss limit on intraday evals.|106000|8
Apex Trader Funding|Evaluation intraday (Rithmic/WealthCharts)|150000|4000|INTRADAY|0|9000|4.0. Threshold fixes at the target profit balance, reached when your peak balance is target balance + drawdown. No daily loss limit on intraday evals.|159000|12
Apex Trader Funding|Evaluation intraday (Tradovate)|25000|1000|INTRADAY|0|1500|4.0. Tradovate evals trail forever - no lock. No daily loss limit on intraday evals.|0|4
Apex Trader Funding|Evaluation intraday (Tradovate)|50000|2000|INTRADAY|0|3000|4.0. Tradovate evals trail forever - no lock. No daily loss limit on intraday evals.|0|6
Apex Trader Funding|Evaluation intraday (Tradovate)|100000|3000|INTRADAY|0|6000|4.0. Tradovate evals trail forever - no lock. No daily loss limit on intraday evals.|0|8
Apex Trader Funding|Evaluation intraday (Tradovate)|150000|4000|INTRADAY|0|9000|4.0. Tradovate evals trail forever - no lock. No daily loss limit on intraday evals.|0|12
Apex Trader Funding|Evaluation end-of-day (Rithmic/WealthCharts)|25000|1000|EOD|500|1500|4.0. EOD evals DO lock on Rithmic - at the target profit balance. Apex publishes a $500 daily loss limit.|26500|4
Apex Trader Funding|Evaluation end-of-day (Rithmic/WealthCharts)|50000|2000|EOD|1000|3000|4.0. EOD evals DO lock on Rithmic - at the target profit balance. Apex publishes a $1,000 daily loss limit.|53000|6
Apex Trader Funding|Evaluation end-of-day (Rithmic/WealthCharts)|100000|3000|EOD|1500|6000|4.0. EOD evals DO lock on Rithmic - at the target profit balance. Apex publishes a $1,500 daily loss limit.|106000|8
Apex Trader Funding|Evaluation end-of-day (Rithmic/WealthCharts)|150000|4000|EOD|2000|9000|4.0. EOD evals DO lock on Rithmic - at the target profit balance. Apex publishes a $2,000 daily loss limit.|159000|12
Apex Trader Funding|Evaluation end-of-day (Tradovate)|25000|1000|EOD|500|1500|4.0. Tradovate EOD evals trail forever - no lock. Apex publishes a $500 daily loss limit.|0|4
Apex Trader Funding|Evaluation end-of-day (Tradovate)|50000|2000|EOD|1000|3000|4.0. Tradovate EOD evals trail forever - no lock. Apex publishes a $1,000 daily loss limit.|0|6
Apex Trader Funding|Evaluation end-of-day (Tradovate)|100000|3000|EOD|1500|6000|4.0. Tradovate EOD evals trail forever - no lock. Apex publishes a $1,500 daily loss limit.|0|8
Apex Trader Funding|Evaluation end-of-day (Tradovate)|150000|4000|EOD|2000|9000|4.0. Tradovate EOD evals trail forever - no lock. Apex publishes a $2,000 daily loss limit.|0|12
Apex Trader Funding|PA / funded (intraday)|25000|1000|INTRADAY|0|0|4.0 funded. Threshold stops at start + $100, reached when your peak balance is start + drawdown + $100. Platform does not matter on funded accounts. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|25100|2
Apex Trader Funding|PA / funded (intraday)|50000|2000|INTRADAY|0|0|4.0 funded. Threshold stops at start + $100, reached when your peak balance is start + drawdown + $100. Platform does not matter on funded accounts. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|50100|4
Apex Trader Funding|PA / funded (intraday)|100000|3000|INTRADAY|0|0|4.0 funded. Threshold stops at start + $100, reached when your peak balance is start + drawdown + $100. Platform does not matter on funded accounts. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|100100|6
Apex Trader Funding|PA / funded (intraday)|150000|4000|INTRADAY|0|0|4.0 funded. Threshold stops at start + $100, reached when your peak balance is start + drawdown + $100. Platform does not matter on funded accounts. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|150100|10
Apex Trader Funding|PA / funded (end-of-day)|25000|1000|EOD|0|0|4.0 funded. Threshold stops at start + $100. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|25100|2
Apex Trader Funding|PA / funded (end-of-day)|50000|2000|EOD|0|0|4.0 funded. Threshold stops at start + $100. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|50100|4
Apex Trader Funding|PA / funded (end-of-day)|100000|3000|EOD|0|0|4.0 funded. Threshold stops at start + $100. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|100100|6
Apex Trader Funding|PA / funded (end-of-day)|150000|4000|EOD|0|0|4.0 funded. Threshold stops at start + $100. Apex sets a TIER-BASED daily loss limit - read yours off your dashboard and type it in.|150100|10


# -- Apex Trader Funding (LEGACY accounts) ------------------------------------
# Drawdowns and contract caps below are from Apex's own "Legacy Evaluation
# Rules" page. Profit targets are the long-standing published figures and are
# less firmly sourced than the drawdowns - the drawdown is what decides whether
# the account lives, so that is the number that was verified.
#
# 25K / 50K / 100K / 150K exist in BOTH 4.0 and legacy with DIFFERENT drawdowns
# (a legacy 50K trails $2,500, a 4.0 50K trails $2,000). Balance alone cannot
# tell them apart, so auto-match picks the tighter 4.0 figure and says so. If
# your account is legacy, choose the legacy row here by hand.
#
# Legacy evaluations follow the same platform split as 4.0 evaluations: the
# threshold fixes at the target profit balance on Rithmic/WealthCharts and
# trails forever on Tradovate.
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|25000|1500|INTRADAY|0|1500|Legacy. Threshold fixes at the target profit balance. Apex cap 4 minis.|26500|4
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|50000|2500|INTRADAY|0|3000|Legacy. Threshold fixes at the target profit balance. Apex cap 10 minis.|53000|10
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|75000|2750|INTRADAY|0|4250|Legacy. Threshold fixes at the target profit balance. Apex cap 12 minis.|79250|12
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|100000|3000|INTRADAY|0|6000|Legacy. Threshold fixes at the target profit balance. Apex cap 14 minis.|106000|14
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|150000|5000|INTRADAY|0|9000|Legacy. Threshold fixes at the target profit balance. Apex cap 17 minis.|159000|17
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|250000|6500|INTRADAY|0|15000|Legacy. Threshold fixes at the target profit balance. Apex cap 27 minis.|265000|27
Apex Trader Funding|Legacy evaluation (Rithmic/WealthCharts)|300000|7500|INTRADAY|0|20000|Legacy. Threshold fixes at the target profit balance. Apex cap 35 minis.|320000|35
Apex Trader Funding|Legacy evaluation (Tradovate)|25000|1500|INTRADAY|0|1500|Legacy. Trails forever on Tradovate. Apex cap 4 minis.|0|4
Apex Trader Funding|Legacy evaluation (Tradovate)|50000|2500|INTRADAY|0|3000|Legacy. Trails forever on Tradovate. Apex cap 10 minis.|0|10
Apex Trader Funding|Legacy evaluation (Tradovate)|75000|2750|INTRADAY|0|4250|Legacy. Trails forever on Tradovate. Apex cap 12 minis.|0|12
Apex Trader Funding|Legacy evaluation (Tradovate)|100000|3000|INTRADAY|0|6000|Legacy. Trails forever on Tradovate. Apex cap 14 minis.|0|14
Apex Trader Funding|Legacy evaluation (Tradovate)|150000|5000|INTRADAY|0|9000|Legacy. Trails forever on Tradovate. Apex cap 17 minis.|0|17
Apex Trader Funding|Legacy evaluation (Tradovate)|250000|6500|INTRADAY|0|15000|Legacy. Trails forever on Tradovate. Apex cap 27 minis.|0|27
Apex Trader Funding|Legacy evaluation (Tradovate)|300000|7500|INTRADAY|0|20000|Legacy. Trails forever on Tradovate. Apex cap 35 minis.|0|35
Apex Trader Funding|Legacy PA / funded|25000|1500|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|25100|4
Apex Trader Funding|Legacy PA / funded|50000|2500|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|50100|10
Apex Trader Funding|Legacy PA / funded|75000|2750|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|75100|12
Apex Trader Funding|Legacy PA / funded|100000|3000|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|100100|14
Apex Trader Funding|Legacy PA / funded|150000|5000|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|150100|17
Apex Trader Funding|Legacy PA / funded|250000|6500|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|250100|27
Apex Trader Funding|Legacy PA / funded|300000|7500|INTRADAY|0|0|Legacy funded. Threshold stops rising at starting balance + $100.|300100|35
Apex Trader Funding|Legacy 100K Static|100000|625|EOD|0|2000|Legacy STATIC drawdown - the floor never moves. Apex cap 2 minis.|99375|2

# -- Topstep (Trading Combine) ------------------------------------------------
# End-of-day trailing max loss, plus a genuine intraday daily loss limit.
# Unlike Apex, Topstep DOES lock during the evaluation: their help centre says
# the Maximum Loss Limit "locks permanently" once it reaches the starting
# balance, and that is the Trading Combine, not just the funded account.
Topstep|Trading Combine|50000|2000|EOD|1000|3000|Daily loss limit resets 5:00pm CT.|50000
Topstep|Trading Combine|100000|3000|EOD|2000|6000|Daily loss limit resets 5:00pm CT.|100000
Topstep|Trading Combine|150000|4500|EOD|3000|9000|Daily loss limit resets 5:00pm CT.|150000

# ── Take Profit Trader ───────────────────────────────────────────────────────
# Test and PRO+ use end-of-day trailing. PRO uses INTRADAY - the drawdown moves
# on unrealised gains, so a winner that round-trips still ratchets your floor.
Take Profit Trader|Test (evaluation)|25000|1500|EOD|0|1500|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|50000|2000|EOD|0|3000|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|75000|2500|EOD|0|4500|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|100000|3000|EOD|0|6000|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|150000|4500|EOD|0|9000|No daily loss limit on Test accounts.|0
Take Profit Trader|PRO (funded)|25000|1500|INTRADAY|0|0|PRO drawdown moves in real time on unrealised gains.|0
Take Profit Trader|PRO (funded)|50000|2000|INTRADAY|0|0|PRO drawdown moves in real time on unrealised gains.|0
Take Profit Trader|PRO (funded)|75000|2500|INTRADAY|0|0|PRO drawdown moves in real time on unrealised gains.|0
Take Profit Trader|PRO (funded)|100000|3500|INTRADAY|0|0|PRO drawdown moves in real time on unrealised gains.|0
Take Profit Trader|PRO (funded)|150000|4500|INTRADAY|0|0|PRO drawdown moves in real time on unrealised gains.|0
Take Profit Trader|PRO+ (live)|25000|1500|EOD|0|0|PRO+ reverts to end-of-day trailing.|0
Take Profit Trader|PRO+ (live)|50000|2000|EOD|0|0|PRO+ reverts to end-of-day trailing.|0
Take Profit Trader|PRO+ (live)|75000|2500|EOD|0|0|PRO+ reverts to end-of-day trailing.|0
Take Profit Trader|PRO+ (live)|100000|3500|EOD|0|0|PRO+ reverts to end-of-day trailing.|0
Take Profit Trader|PRO+ (live)|150000|4500|EOD|0|0|PRO+ reverts to end-of-day trailing.|0

# ── MyFundedFutures ──────────────────────────────────────────────────────────
# No daily loss limit on Rapid or Pro; Builder has a $1,000 soft pause.
MyFundedFutures|Builder|50000|2000|EOD|1000|3000|$1,000 soft-pause daily loss; $1,500 max-loss option at checkout.|50100
MyFundedFutures|Rapid (intraday)|25000|1000|INTRADAY|0|1500|Trail updates on every new equity high during the session.|25100
MyFundedFutures|Rapid (intraday)|50000|2000|INTRADAY|0|3000|Trail updates on every new equity high during the session.|50100
MyFundedFutures|Rapid (intraday)|100000|3000|INTRADAY|0|6000|Trail updates on every new equity high during the session.|100100
MyFundedFutures|Rapid (intraday)|150000|4500|INTRADAY|0|9000|Trail updates on every new equity high during the session.|150100
MyFundedFutures|Pro (EOD)|50000|2000|EOD|0|3000|End-of-day trailing.|50100
MyFundedFutures|Pro (EOD)|100000|3000|EOD|0|6000|End-of-day trailing.|100100
MyFundedFutures|Pro (EOD)|150000|4500|EOD|0|9000|End-of-day trailing.|150100
MyFundedFutures|Core (legacy)|50000|1500|EOD|0|3000|Legacy plan - verify, no longer sold.|50100
MyFundedFutures|Flex (legacy, static)|25000|1000|EOD|0|0|Legacy STATIC drawdown - does not trail. Verify.|25100
MyFundedFutures|Flex (legacy, static)|50000|2000|EOD|0|0|Legacy STATIC drawdown - does not trail. Verify.|50100

# -- Bulenox --------------------------------------------------------------------
# Rithmic feed; usable on NinjaTrader 8. Two eval options per size: "No Scaling"
# (intraday trailing, full contracts from the start) and "EOD" (end-of-day
# trailing, dynamic scaling). Bulenox publishes a lock only for the funded/Master
# EOD account (stops at initial balance + $100); no lock is published for the
# evaluation trailing drawdown, so LOCKAT stays 0. No daily loss limit stated for
# the evaluation.
Bulenox|Evaluation No-Scaling (intraday)|25000|1500|INTRADAY|0|1500|Rithmic/NT8. Trails peak; no published eval lock. Cap 3 minis.|0|3
Bulenox|Evaluation No-Scaling (intraday)|50000|2500|INTRADAY|0|3000|Rithmic/NT8. Trails peak; no published eval lock. Cap 7 minis.|0|7
Bulenox|Evaluation No-Scaling (intraday)|100000|3000|INTRADAY|0|6000|Rithmic/NT8. Trails peak; no published eval lock. Cap 12 minis.|0|12
Bulenox|Evaluation No-Scaling (intraday)|150000|4500|INTRADAY|0|9000|Rithmic/NT8. Trails peak; no published eval lock. Cap 15 minis.|0|15
Bulenox|Evaluation No-Scaling (intraday)|250000|5500|INTRADAY|0|15000|Rithmic/NT8. Trails peak; no published eval lock. Cap 25 minis.|0|25
Bulenox|Evaluation EOD|25000|1500|EOD|0|1500|Rithmic/NT8. EOD trailing; eval lock unconfirmed. Cap 3 minis.|0|3
Bulenox|Evaluation EOD|50000|2500|EOD|0|3000|Rithmic/NT8. EOD trailing; eval lock unconfirmed. Cap 7 minis.|0|7
Bulenox|Evaluation EOD|100000|3000|EOD|0|6000|Rithmic/NT8. EOD trailing; eval lock unconfirmed. Cap 12 minis.|0|12
Bulenox|Evaluation EOD|150000|4500|EOD|0|9000|Rithmic/NT8. EOD trailing; eval lock unconfirmed. Cap 15 minis.|0|15
Bulenox|Evaluation EOD|250000|5500|EOD|0|15000|Rithmic/NT8. EOD trailing; eval lock unconfirmed. Cap 25 minis.|0|25

# -- Tradeify -------------------------------------------------------------------
# Tradovate feed; usable on NinjaTrader. All plans use END-OF-DAY trailing off the
# highest EOD balance. Drawdown locks (start + $100) ONLY once funded (Sim Funded),
# NOT during the evaluation - so LOCKAT stays 0 for these eval rows. Daily loss
# limits are "soft" (lock the day, do not fail the account). Caps for accounts
# bought after 12 Sep 2025.
Tradeify|Growth evaluation (EOD)|25000|1000|EOD|600|1500|Tradovate/NT. Soft daily loss. Locks start+100 only once funded. Cap 1 mini.|0|1
Tradeify|Growth evaluation (EOD)|50000|2000|EOD|1250|3000|Tradovate/NT. Soft daily loss. Locks once funded. Cap 4 minis.|0|4
Tradeify|Growth evaluation (EOD)|100000|3500|EOD|2500|6000|Tradovate/NT. Soft daily loss. Locks once funded. Cap 8 minis.|0|8
Tradeify|Growth evaluation (EOD)|150000|5000|EOD|3000|9000|Tradovate/NT. Soft daily loss. Locks once funded. Cap 12 minis.|0|12

# -- TradeDay -------------------------------------------------------------------
# CQG feed via Tradovate; free NinjaTrader 8 provided. Each size sold in an
# Intraday and an End-of-Day version. Trailing max drawdown trails the peak then
# FREEZES at the starting balance (their own example: a 100K starts TMD at 97,000
# and freezes once balance hits 103,000). No separate daily loss limit.
TradeDay|Evaluation (intraday)|50000|2000|INTRADAY|0|3000|CQG/NT8. TMD freezes at starting balance. Cap 5 minis.|50000|5
TradeDay|Evaluation (intraday)|100000|3000|INTRADAY|0|6000|CQG/NT8. TMD freezes at starting balance. Cap 10 minis.|100000|10
TradeDay|Evaluation (intraday)|150000|4500|INTRADAY|0|9000|CQG/NT8. TMD freezes at starting balance. Cap 15 minis.|150000|15
TradeDay|Evaluation (end-of-day)|50000|2000|EOD|0|3000|CQG/NT8. TMD freezes at starting balance. Cap 5 minis.|50000|5
TradeDay|Evaluation (end-of-day)|100000|3000|EOD|0|6000|CQG/NT8. TMD freezes at starting balance. Cap 10 minis.|100000|10
TradeDay|Evaluation (end-of-day)|150000|4500|EOD|0|9000|CQG/NT8. TMD freezes at starting balance. Cap 15 minis.|150000|15

# -- Earn2Trade -----------------------------------------------------------------
# Rithmic or Tradovate feed; usable on NinjaTrader (all provided free). Current
# evaluations use END-OF-DAY drawdown that trails up only and FREEZES at the
# starting balance. Sold as Trader Career Path (TCP) and Gauntlet Mini (GAU);
# same-size specs match. Only sizes verified from Earn2Trade's own pages are
# listed (GAU150/200 figures were image-only and are omitted).
Earn2Trade|Trader Career Path / Gauntlet (EOD)|25000|1500|EOD|550|1750|Rithmic/Tradovate/NT. Freezes at starting balance. Cap 3 minis.|25000|3
Earn2Trade|Trader Career Path / Gauntlet (EOD)|50000|2000|EOD|1100|3000|Rithmic/Tradovate/NT. Freezes at starting balance. Cap 6 minis.|50000|6
Earn2Trade|Trader Career Path / Gauntlet (EOD)|100000|3500|EOD|2200|6000|Rithmic/Tradovate/NT. Freezes at starting balance. Cap 12 minis.|100000|12

# -- Legends Trading ------------------------------------------------------------
# Rithmic/Tradovate; usable on NinjaTrader (listed in NinjaTrader's own prop-firm
# directory). Max drawdown is calculated END OF DAY. The firm does not publish
# whether the trailing drawdown locks at the starting balance, so LOCKAT stays 0
# (assume it trails = understates room) until confirmed. Two eval families:
# Apprentice (subscription) and Elite (one-time).
Legends Trading|Apprentice evaluation (EOD)|25000|1500|EOD|0|1500|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 4 minis.|0|4
Legends Trading|Apprentice evaluation (EOD)|50000|2000|EOD|0|3000|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 10 minis.|0|10
Legends Trading|Apprentice evaluation (EOD)|100000|3000|EOD|0|6000|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 14 minis.|0|14
Legends Trading|Apprentice evaluation (EOD)|150000|4000|EOD|0|9000|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 17 minis.|0|17
Legends Trading|Elite evaluation (EOD)|25000|1250|EOD|0|1500|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 2 minis.|0|2
Legends Trading|Elite evaluation (EOD)|50000|2200|EOD|0|2700|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 4 minis.|0|4
Legends Trading|Elite evaluation (EOD)|100000|3000|EOD|0|6000|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 8 minis.|0|8
Legends Trading|Elite evaluation (EOD)|150000|4500|EOD|0|9000|Rithmic/Tradovate/NT. EOD trailing; lock unconfirmed. Cap 12 minis.|0|12

# -- Elite Trader Funding -------------------------------------------------------
# NinjaTrader / Rithmic / Tradovate. Verified live from ETF's own pricing widget
# (elitetraderfunding.app/evaluations, 1 Aug 2026). Trailing plans lock at
# start+$100 once the "safety net" (max drawdown + $100 realized) is reached, so
# LOCKAT = size+100. Static never trails (fixed floor = size - max loss). Only the
# sizes read directly are listed; 1-Step 250K, EOD 150K, Static 25K/50K, Diamond
# Hands, Direct-to-Funded and Fast Track are not yet added.
Elite Trader Funding|1-Step (Live Trailing, intraday)|50000|2000|INTRADAY|0|3000|NT/Rithmic/Tradovate. Locks at start+$100 after safety net. Cap 8 minis.|50100|8
Elite Trader Funding|1-Step (Live Trailing, intraday)|100000|3000|INTRADAY|0|6000|NT/Rithmic/Tradovate. Locks at start+$100 after safety net. Cap 14 minis.|100100|14
Elite Trader Funding|1-Step (Live Trailing, intraday)|150000|5000|INTRADAY|0|9000|NT/Rithmic/Tradovate. Locks at start+$100 after safety net. Cap 18 minis.|150100|18
Elite Trader Funding|End of Day (EOD trailing)|50000|2000|EOD|1100|3000|NT/Rithmic/Tradovate. Locks at start+$100 after safety net. Cap 8 minis.|50100|8
Elite Trader Funding|End of Day (EOD trailing)|100000|3500|EOD|2200|6000|NT/Rithmic/Tradovate. Locks at start+$100 after safety net. Cap 14 minis.|100100|14
Elite Trader Funding|Static (fixed floor)|10000|500|EOD|0|1000|NT/Rithmic/Tradovate. Fixed floor, never trails. Cap 1 mini.|9500|1

# -- Your own account (not a prop firm) ---------------------------------------
# For an Interactive Brokers, NinjaTrader Brokerage or any self-funded account.
# There is no trailing drawdown here - LOCKAT is set to size minus max loss, which
# freezes the floor immediately, so it behaves as a FIXED max loss you choose.
# Pick the nearest size and then edit the numbers in Rules to match reality.
My own account (not a prop firm)|Self-funded - 5% max loss|10000|500|EOD|250|0|Fixed max loss, does not trail. Edit to match your own risk.|9500
My own account (not a prop firm)|Self-funded - 5% max loss|25000|1250|EOD|500|0|Fixed max loss, does not trail. Edit to match your own risk.|23750
My own account (not a prop firm)|Self-funded - 5% max loss|50000|2500|EOD|1000|0|Fixed max loss, does not trail. Edit to match your own risk.|47500
My own account (not a prop firm)|Self-funded - 5% max loss|100000|5000|EOD|2000|0|Fixed max loss, does not trail. Edit to match your own risk.|95000
My own account (not a prop firm)|Self-funded - 10% max loss|10000|1000|EOD|250|0|Fixed max loss, does not trail. Edit to match your own risk.|9000
My own account (not a prop firm)|Self-funded - 10% max loss|25000|2500|EOD|500|0|Fixed max loss, does not trail. Edit to match your own risk.|22500
My own account (not a prop firm)|Self-funded - 10% max loss|50000|5000|EOD|1000|0|Fixed max loss, does not trail. Edit to match your own risk.|45000
My own account (not a prop firm)|Self-funded - 10% max loss|100000|10000|EOD|2000|0|Fixed max loss, does not trail. Edit to match your own risk.|90000
`;
