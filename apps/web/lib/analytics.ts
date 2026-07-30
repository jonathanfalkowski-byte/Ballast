// ─────────────────────────────────────────────────────────────────────────────
// analytics.ts — Behavioural edge analysis and process grading
//
// Two things almost no trading journal does properly:
//
//  1. Edge BY BEHAVIOUR TAG. Not "what's my win rate" but "what do my revenge trades
//     actually cost me". Research on prop traders found revenge-tagged trades running
//     deeply negative expectancy while the same trader's planned trades were positive —
//     the average hides the whole story.
//
//  2. Grading the DAY ON PROCESS, not P&L. A green day with broken rules is a failing
//     day, because it trains the behaviour that eventually empties the account.
//
// Pure functions — no framework, no I/O — so they're directly testable.
// ─────────────────────────────────────────────────────────────────────────────

export type Tag = "a_plus" | "plan" | "revenge" | "fomo" | "boredom";

export const TAG_LABEL: Record<Tag, string> = {
  a_plus: "A+ setup",
  plan: "By the plan",
  revenge: "Revenge",
  fomo: "FOMO",
  boredom: "Boredom",
};

// Tags that represent trading the plan vs trading an emotion.
export const DISCIPLINED_TAGS: Tag[] = ["a_plus", "plan"];
export const EMOTIONAL_TAGS: Tag[] = ["revenge", "fomo", "boredom"];

export type TradeLike = {
  pnl: number;
  tag?: string | null;
};

export type TagEdge = {
  tag: string;
  label: string;
  n: number;
  wins: number;
  winRate: number;
  total: number; // total P&L contributed
  expectancy: number; // per-trade
  share: number; // share of all trades, 0..1
};

/** Expectancy broken out per behavioural tag — the "what does this habit cost me" view. */
export function edgeByTag(trades: TradeLike[]): TagEdge[] {
  const groups = new Map<string, TradeLike[]>();
  for (const t of trades) {
    const key = (t.tag ?? "untagged") as string;
    const arr = groups.get(key);
    if (arr) arr.push(t);
    else groups.set(key, [t]);
  }

  const out: TagEdge[] = [];
  for (const [tag, list] of groups) {
    const wins = list.filter((t) => t.pnl > 0).length;
    const total = list.reduce((s, t) => s + t.pnl, 0);
    out.push({
      tag,
      label: TAG_LABEL[tag as Tag] ?? "Untagged",
      n: list.length,
      wins,
      winRate: list.length ? wins / list.length : 0,
      total,
      expectancy: list.length ? total / list.length : 0,
      share: trades.length ? list.length / trades.length : 0,
    });
  }
  // Worst expectancy first — the point is to surface what's hurting, not to flatter.
  return out.sort((a, b) => a.expectancy - b.expectancy);
}

export type BehaviourCost = {
  emotionalTrades: number;
  emotionalPnl: number; // usually negative — the cost of undisciplined trades
  disciplinedTrades: number;
  disciplinedPnl: number;
  /** What the account would look like if the emotional trades had never been taken. */
  pnlWithoutEmotional: number;
  actualPnl: number;
  /** Positive number = dollars given away to emotional trading. */
  costOfEmotionalTrading: number;
};

/**
 * The headline that makes people share a screenshot:
 * "your revenge/FOMO/boredom trades cost you $X this month."
 */
export function behaviourCost(trades: TradeLike[]): BehaviourCost {
  const emotional = trades.filter((t) => EMOTIONAL_TAGS.includes((t.tag ?? "") as Tag));
  const disciplined = trades.filter((t) => DISCIPLINED_TAGS.includes((t.tag ?? "") as Tag));
  const emotionalPnl = emotional.reduce((s, t) => s + t.pnl, 0);
  const disciplinedPnl = disciplined.reduce((s, t) => s + t.pnl, 0);
  const actualPnl = trades.reduce((s, t) => s + t.pnl, 0);
  return {
    emotionalTrades: emotional.length,
    emotionalPnl,
    disciplinedTrades: disciplined.length,
    disciplinedPnl,
    pnlWithoutEmotional: actualPnl - emotionalPnl,
    actualPnl,
    costOfEmotionalTrading: -emotionalPnl,
  };
}

// ── Process grading ───────────────────────────────────────────────────────────

export type RuleAdherence = {
  stoppedAtMaxLosses: boolean;
  respectedDailyLossLimit: boolean;
  respectedMaxTrades: boolean;
  respectedSize: boolean;
  tradedInWindowOnly: boolean;
  noEmotionalTrades: boolean;
};

export type DayGrade = {
  grade: "A" | "B" | "C" | "D" | "F";
  followed: number;
  total: number;
  pass: boolean; // rule-compliant day, regardless of P&L
  headline: string;
  note: string;
};

/**
 * Grade the day on rule adherence ONLY. P&L is passed in purely so the summary can make
 * the point explicitly: a green day with broken rules is still a failing day.
 */
export function gradeDay(adherence: RuleAdherence, dailyPnl: number): DayGrade {
  const checks = Object.values(adherence);
  const followed = checks.filter(Boolean).length;
  const total = checks.length;
  const pct = total ? followed / total : 0;

  let grade: DayGrade["grade"];
  if (pct === 1) grade = "A";
  else if (pct >= 0.83) grade = "B";
  else if (pct >= 0.66) grade = "C";
  else if (pct >= 0.5) grade = "D";
  else grade = "F";

  const pass = followed === total;
  const green = dailyPnl > 0;

  let headline: string;
  let note: string;
  if (pass && green) {
    headline = "Clean day.";
    note = "Rules followed and green. This is the day worth repeating — the money was a byproduct.";
  } else if (pass && !green) {
    headline = "Clean day — a passing day.";
    note = "You followed every rule and still finished red. That's a good day. Losses inside the plan are the cost of doing business; the process held.";
  } else if (!pass && green) {
    headline = "Green, but a failing day.";
    note = "You made money while breaking your own rules. This is the most dangerous kind of day, because it teaches you that breaking rules pays. It won't keep paying.";
  } else {
    headline = "Rules broken, and red.";
    note = "The rules exist for exactly this day. Look at which one broke first — that's usually where the whole day turned.";
  }

  return { grade, followed, total, pass, headline, note };
}

/** Longest current run of consecutive rule-compliant days, most recent last. */
export function cleanDayStreak(days: Array<{ pass: boolean }>): number {
  let streak = 0;
  for (let i = days.length - 1; i >= 0; i--) {
    if (days[i].pass) streak++;
    else break;
  }
  return streak;
}

export function money(n: number): string {
  const r = Math.round(n);
  return (r < 0 ? "-$" : "$") + Math.abs(r).toLocaleString();
}
