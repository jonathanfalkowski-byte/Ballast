// Turns a discipline decision into a human, command-style narrative.
// Mirrors Velocity's decision-language.ts + the "<Action> — <Reason>" headline pattern from Feature.md.

import type { DisciplineAction, DisciplineFactor } from "./disciplineEngine";

const ACTION_VERB: Record<DisciplineAction, string> = {
  stop_for_day: "Stop",
  lockout: "Lock out",
  cooldown: "Step away",
  size_down: "Size down",
  protect_green: "Protect it",
  trade: "Clear to trade",
  none: "Hold",
};

export type DisciplineNarrative = {
  headline: string; // "Stop — you've hit your 2nd loss"
  bullets: string[];
};

export function createDisciplineNarrative(
  action: DisciplineAction,
  reason: string,
  factors: DisciplineFactor[],
): DisciplineNarrative {
  const verb = ACTION_VERB[action] ?? "Hold";
  const headline = `${verb} — ${reason}`;
  const bullets = factors
    .slice()
    .sort((a, b) => severityRank(b.severity) - severityRank(a.severity))
    .map((f) => f.summary);
  return { headline, bullets };
}

function severityRank(s: "low" | "medium" | "high"): number {
  return s === "high" ? 3 : s === "medium" ? 2 : 1;
}
