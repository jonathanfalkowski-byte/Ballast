// ─────────────────────────────────────────────────────────────────────────────
// readiness.ts — Challenge-readiness assessment
//
// The honest pre-purchase tool: a trader pastes their past/sim trade P&Ls and we
// (1) compute edge statistics and (2) run a rule-aware Monte Carlo that bootstraps
// from their OWN trade distribution to estimate the probability of reaching a payout
// target before breaching the account's drawdown / daily-loss rules.
//
// The whole point (per the research) is that this can honestly tell someone:
// "you haven't demonstrated an edge yet — don't pay for another evaluation."
//
// Pure functions, no framework — so they can be unit-tested directly.
// ─────────────────────────────────────────────────────────────────────────────

export type TradeStats = {
  n: number;
  wins: number;
  losses: number;
  winRate: number; // 0..1
  avgWin: number;
  avgLoss: number; // negative
  expectancy: number; // avg P&L per trade
  profitFactor: number; // gross win / gross loss
  total: number;
};

export function computeStats(pnls: number[]): TradeStats {
  const n = pnls.length;
  if (n === 0) {
    return { n: 0, wins: 0, losses: 0, winRate: 0, avgWin: 0, avgLoss: 0, expectancy: 0, profitFactor: 0, total: 0 };
  }
  const wins = pnls.filter((p) => p > 0);
  const losses = pnls.filter((p) => p < 0);
  const grossWin = wins.reduce((s, p) => s + p, 0);
  const grossLoss = Math.abs(losses.reduce((s, p) => s + p, 0));
  const total = pnls.reduce((s, p) => s + p, 0);
  return {
    n,
    wins: wins.length,
    losses: losses.length,
    winRate: wins.length / n,
    avgWin: wins.length ? grossWin / wins.length : 0,
    avgLoss: losses.length ? -grossLoss / losses.length : 0,
    expectancy: total / n,
    profitFactor: grossLoss > 0 ? grossWin / grossLoss : (grossWin > 0 ? Infinity : 0),
    total,
  };
}

export type AccountRules = {
  trailingDrawdown: number; // e.g. 2500
  profitTarget: number; // e.g. 3000
  dailyLossLimit: number; // 0 = none (some firms have no separate daily loss limit)
  tradesPerDay: number; // used to group trades for the daily-loss check
  maxTrades: number; // cap a run so it can't loop forever ("timed out")
};

export type MonteCarloResult = {
  runs: number;
  pSuccess: number; // reached profit target before any breach
  pDrawdownBreach: number;
  pDailyBreach: number;
  pTimeout: number; // hit maxTrades without success or breach
  medianTradesToOutcome: number;
};

// Deterministic PRNG so results are reproducible for a given input.
function mulberry32(seed: number) {
  let a = seed >>> 0;
  return function () {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function monteCarlo(pnls: number[], rules: AccountRules, runs = 5000, seed = 12345): MonteCarloResult {
  const n = pnls.length;
  if (n === 0) {
    return { runs: 0, pSuccess: 0, pDrawdownBreach: 0, pDailyBreach: 0, pTimeout: 0, medianTradesToOutcome: 0 };
  }
  const rand = mulberry32(seed);
  let success = 0, ddBreach = 0, dailyBreach = 0, timeout = 0;
  const tradesToOutcome: number[] = [];

  for (let r = 0; r < runs; r++) {
    let balance = 0;
    let peak = 0;
    let dayPnl = 0;
    let tradesInDay = 0;
    let outcome: "success" | "dd" | "daily" | "timeout" = "timeout";
    let t = 0;
    for (; t < rules.maxTrades; t++) {
      const pnl = pnls[Math.floor(rand() * n)];
      balance += pnl;
      dayPnl += pnl;
      tradesInDay++;
      if (balance > peak) peak = balance;

      // Trailing drawdown: floor follows the peak; start floor at -trailingDrawdown.
      const floor = peak - rules.trailingDrawdown;
      if (balance <= floor) { outcome = "dd"; break; }

      // Daily loss limit checked at the close of each simulated day.
      if (rules.dailyLossLimit > 0 && tradesInDay >= rules.tradesPerDay) {
        if (dayPnl <= -rules.dailyLossLimit) { outcome = "daily"; break; }
        dayPnl = 0; tradesInDay = 0;
      }

      if (balance >= rules.profitTarget) { outcome = "success"; break; }
    }
    tradesToOutcome.push(t + 1);
    if (outcome === "success") success++;
    else if (outcome === "dd") ddBreach++;
    else if (outcome === "daily") dailyBreach++;
    else timeout++;
  }

  tradesToOutcome.sort((a, b) => a - b);
  const median = tradesToOutcome[Math.floor(tradesToOutcome.length / 2)] || 0;

  return {
    runs,
    pSuccess: success / runs,
    pDrawdownBreach: ddBreach / runs,
    pDailyBreach: dailyBreach / runs,
    pTimeout: timeout / runs,
    medianTradesToOutcome: median,
  };
}

export type Verdict = {
  level: "not_enough_data" | "no_edge" | "not_ready" | "borderline" | "ready";
  headline: string;
  detail: string;
};

export function verdict(stats: TradeStats, mc: MonteCarloResult): Verdict {
  if (stats.n < 30) {
    return {
      level: "not_enough_data",
      headline: "Not enough data yet",
      detail: `Only ${stats.n} trades. You need roughly 30–50+ before the numbers mean anything. Keep trading in sim and come back — reading into this sample would fool you.`,
    };
  }
  if (stats.expectancy <= 0) {
    return {
      level: "no_edge",
      headline: "No edge demonstrated — don't buy an evaluation yet",
      detail: `Across ${stats.n} trades your average result is ${money(stats.expectancy)} per trade — that's flat-to-negative. On these numbers an evaluation is more likely to fail than pass. The honest move is to keep the money and keep working on the process until the expectancy turns clearly positive.`,
    };
  }
  const p = mc.pSuccess;
  if (p >= 0.6) {
    return {
      level: "ready",
      headline: "Reasonable shot — but no guarantees",
      detail: `Your own trade distribution reaches the target before a breach in ${(p * 100).toFixed(0)}% of simulations. That's a real chance. Size to the rules, protect the cushion, and treat any single attempt as one sample.`,
    };
  }
  if (p >= 0.35) {
    return {
      level: "borderline",
      headline: "Borderline — closer to a coin flip",
      detail: `The simulation reaches payout before a breach only ${(p * 100).toFixed(0)}% of the time. Tighten risk per trade or improve the edge before paying — small changes to your loss size move this number a lot.`,
    };
  }
  return {
    level: "not_ready",
    headline: "Not ready — the math says most attempts breach first",
    detail: `Only ${(p * 100).toFixed(0)}% of simulations reach the target before breaching the drawdown or daily-loss rule. Buying an evaluation now most likely donates the fee. Work on risk and edge first.`,
  };
}

export function money(n: number): string {
  const r = Math.round(n);
  return (r < 0 ? "-$" : "$") + Math.abs(r).toLocaleString();
}

// ── Parsing ───────────────────────────────────────────────────────────────────
// Handles: one number per line (commas are thousands separators, e.g. $1,000),
// parentheses negatives e.g. (200), and CSVs with a pnl-like column.
function parseNum(s: string): number | null {
  let v = (s ?? "").trim();
  if (!v) return null;
  let neg = false;
  if (/^\(.*\)$/.test(v)) { neg = true; v = v.slice(1, -1); }
  v = v.replace(/[$,\s]/g, "");
  if (v.startsWith("-")) { neg = true; v = v.slice(1); }
  const num = parseFloat(v);
  if (isNaN(num)) return null;
  return neg ? -Math.abs(num) : num;
}

export function parsePnls(raw: string): number[] {
  if (!raw.trim()) return [];
  const lines = raw.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  // Treat as CSV if lines carry alphabetic content (headers, dates, symbols) or multiple delimited fields.
  const sample = lines.slice(0, 3).join("\n");
  const looksCsv = /[a-df-z]/i.test(sample.replace(/e[-+]?\d/gi, "")) || /[,\t;].*[,\t;]/.test(lines[0]);

  if (!looksCsv) {
    const out: number[] = [];
    for (const line of lines) {
      const num = parseNum(line);
      if (num !== null) out.push(num);
    }
    return out;
  }

  const delim = lines[0].includes("\t") ? "\t" : lines[0].includes(";") ? ";" : ",";
  const header = lines[0].toLowerCase().split(delim).map((c) => c.trim());
  let col = header.findIndex((c) => /p&?l|pnl|profit|net|result|realized|gain/.test(c));
  if (col < 0) col = header.length - 1; // fallback: last column
  // It's a header row only if the target column's first value is non-numeric (a label).
  const hasHeader = parseNum(header[col]) === null;

  const out: number[] = [];
  for (let i = hasHeader ? 1 : 0; i < lines.length; i++) {
    const cells = lines[i].split(delim);
    const num = parseNum(cells[col] ?? cells[cells.length - 1] ?? "");
    if (num !== null) out.push(num);
  }
  return out;
}
