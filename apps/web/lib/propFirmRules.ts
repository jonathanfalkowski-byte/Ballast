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
export const RULES_VERSION = 4;

/** Date the figures below were last checked against the firms' own pages. */
export const RULES_VERIFIED = "2026-07-30";

/** Pages a maintenance job should re-check for changes. */
export const RULE_SOURCES: Array<{ firm: string; url: string }> = [
  { firm: "Apex Trader Funding", url: "https://apextraderfunding.com/help-center/" },
  { firm: "Topstep", url: "https://www.topstep.com/express-funded-account-rules" },
  { firm: "Topstep", url: "https://help.topstep.com/en/articles/8284204-what-is-the-maximum-loss-limit" },
  { firm: "Take Profit Trader", url: "https://takeprofittrader.com/" },
  { firm: "MyFundedFutures", url: "https://myfundedfutures.com/" },
];

export const RULES_TEXT = `# Ballast rule book - served from tradeballast.com/api/rules
# Figures are a convenience, not gospel. Always verify against your own firm.
# A wrong drawdown produces a wrong cushion, which is worse than no tool at all.

VERSION|${RULES_VERSION}
VERIFIED|${RULES_VERIFIED}

# -- Apex Trader Funding (4.0, effective 1 March 2026) ------------------------
# Legacy 75K / 250K / 300K sizes were retired. If you hold a legacy account its
# figures WILL differ - enter them by hand.
Apex Trader Funding|Full (intraday)|25000|1000|INTRADAY|0|1500|Trailing threshold is the safeguard; no separate daily loss limit.|25100
Apex Trader Funding|Full (intraday)|50000|2000|INTRADAY|0|3000|Trailing threshold is the safeguard; no separate daily loss limit.|50100
Apex Trader Funding|Full (intraday)|100000|3000|INTRADAY|0|6000|Trailing threshold is the safeguard; no separate daily loss limit.|100100
Apex Trader Funding|Full (intraday)|150000|4000|INTRADAY|0|9000|Trailing threshold is the safeguard; no separate daily loss limit.|150100
Apex Trader Funding|Full (end-of-day)|25000|1000|EOD|0|1500|EOD variant also carries a soft daily loss limit - check your dashboard.|25100
Apex Trader Funding|Full (end-of-day)|50000|2000|EOD|0|3000|EOD variant also carries a soft daily loss limit - check your dashboard.|50100
Apex Trader Funding|Full (end-of-day)|100000|3000|EOD|0|6000|EOD variant also carries a soft daily loss limit - check your dashboard.|100100
Apex Trader Funding|Full (end-of-day)|150000|4000|EOD|0|9000|EOD variant also carries a soft daily loss limit - check your dashboard.|150100

# -- Topstep (Trading Combine) ------------------------------------------------
# End-of-day trailing max loss, plus a genuine intraday daily loss limit.
Topstep|Trading Combine|50000|2000|EOD|1000|3000|Daily loss limit resets 5:00pm CT.|50000
Topstep|Trading Combine|100000|3000|EOD|2000|6000|Daily loss limit resets 5:00pm CT.|100000
Topstep|Trading Combine|150000|4500|EOD|3000|9000|Daily loss limit resets 5:00pm CT.|150000

# -- Take Profit Trader -------------------------------------------------------
# Test and PRO+ use end-of-day trailing. PRO uses INTRADAY - the drawdown moves
# on unrealised gains, so a winner that round-trips still ratchets your floor.
Take Profit Trader|Test (evaluation)|25000|1500|EOD|0|1500|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|50000|2000|EOD|0|3000|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|75000|2500|EOD|0|4500|No daily loss limit on Test accounts.|0
Take Profit Trader|Test (evaluation)|100000|3500|EOD|0|6000|No daily loss limit on Test accounts.|0
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

# -- MyFundedFutures ----------------------------------------------------------
# No daily loss limit on any plan.
MyFundedFutures|Builder|50000|2000|EOD|0|3000|Default max loss; a $1,500 option exists at checkout.|50100
MyFundedFutures|Rapid (4% intraday)|25000|1000|INTRADAY|0|1500|Trail updates on every new equity high during the session.|25100
MyFundedFutures|Rapid (4% intraday)|50000|2000|INTRADAY|0|3000|Trail updates on every new equity high during the session.|50100
MyFundedFutures|Rapid (4% intraday)|100000|4000|INTRADAY|0|6000|Trail updates on every new equity high during the session.|100100
MyFundedFutures|Rapid (4% intraday)|150000|6000|INTRADAY|0|9000|Trail updates on every new equity high during the session.|150100
MyFundedFutures|Pro (3% EOD)|50000|1500|EOD|0|3000|End-of-day trailing.|50100
MyFundedFutures|Pro (3% EOD)|100000|3000|EOD|0|6000|End-of-day trailing.|100100
MyFundedFutures|Pro (3% EOD)|150000|4500|EOD|0|9000|End-of-day trailing.|150100
MyFundedFutures|Core (legacy)|50000|1500|EOD|0|3000|Legacy plan - verify, no longer sold.|50100
MyFundedFutures|Flex (legacy, static)|25000|1000|EOD|0|0|Legacy STATIC drawdown - does not trail. Verify.|25100
MyFundedFutures|Flex (legacy, static)|50000|2000|EOD|0|0|Legacy STATIC drawdown - does not trail. Verify.|50100
`;
