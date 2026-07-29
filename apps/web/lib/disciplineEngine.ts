// ────────────────────────────────────────────────────────────────────────────
// disciplineEngine.ts
//
// The brain of Ballast. This is the trading re-skin of Velocity's decisionEngine.ts:
// take a stream of signals -> detect risk -> pick ONE next action -> explain it ->
// attach confidence/urgency -> (later) learn from whether the trader obeyed.
//
// It is deliberately rule-based and readable. The whole point of the product is that
// a trader trusts it in the tilt moment, so there is NO hidden magic here — every
// recommendation traces to an explicit signal, exactly like Velocity's philosophy
// (broker/trader trust > reliability > explainability > speed > AI sophistication).
// ────────────────────────────────────────────────────────────────────────────

import {
  detectRiskSignals,
  type RiskSignal,
  type DetectRiskSignalsInput,
} from "./riskSignals";
import { createDisciplineNarrative, type DisciplineNarrative } from "./decisionLanguage";

export type DisciplineAction =
  | "trade" // A+ setup, all clear
  | "size_down" // proceed but smaller
  | "cooldown" // hands off, wait out the tilt window
  | "protect_green" // you're up — stop or free-roll
  | "stop_for_day" // done, close the platform
  | "lockout" // hard lock (hit loss limit)
  | "none"; // nothing to act on

export type DisciplineConfidence = "low" | "medium" | "high";
export type DisciplineUrgency = "calm" | "caution" | "alert";

export type DisciplineFactor = {
  key: string;
  severity: "low" | "medium" | "high";
  summary: string;
};

// Same idea as Velocity's DecisionInput: everything the engine needs to decide, flat.
export type DisciplineInput = DetectRiskSignalsInput;

export type DisciplineDecision = {
  action: DisciplineAction;
  urgency: DisciplineUrgency;
  confidence: DisciplineConfidence;
  reason: string;
  narrative: DisciplineNarrative;
  factors: DisciplineFactor[];
  riskSignals: RiskSignal[];
};

/**
 * Evaluate the trader's current state and return the single most important next action.
 * Rules are checked in priority order — the first hard breaker wins, because in a
 * give-back/revenge profile the job is to stop the worst thing first.
 */
export function evaluateDiscipline(input: DisciplineInput): DisciplineDecision {
  const riskSignals = detectRiskSignals(input);
  const factors: DisciplineFactor[] = riskSignals.map((s) => ({
    key: s.key,
    severity: s.severity,
    summary: s.summary,
  }));

  const has = (key: string) => riskSignals.some((s) => s.key === key);

  let action: DisciplineAction = "trade";
  let urgency: DisciplineUrgency = "calm";
  let confidence: DisciplineConfidence = "high";
  let reason = "Conditions are clean — trade your plan.";

  // ── Priority ladder ──────────────────────────────────────────────
  if (has("daily_loss_limit")) {
    action = "lockout";
    urgency = "alert";
    reason = "you've hit your daily loss limit";
  } else if (has("loss_streak")) {
    action = "stop_for_day";
    urgency = "alert";
    reason = "you've taken your max losses for the day";
  } else if (has("give_back")) {
    action = "protect_green";
    urgency = "alert";
    reason = "you're handing back a green day";
  } else if (has("revenge_window")) {
    action = "cooldown";
    urgency = "alert";
    reason = "you just took a loss — wait out the tilt window";
  } else if (has("thin_cushion")) {
    action = "size_down";
    urgency = "caution";
    reason = "your cushion to the trailing floor is thin";
  } else if (has("over_trading")) {
    action = "stop_for_day";
    urgency = "caution";
    reason = "you're at your max trades for the day";
  } else if (has("over_size")) {
    action = "size_down";
    urgency = "caution";
    reason = "you're planning more contracts than your cap";
  } else if (has("out_of_window")) {
    action = "none";
    urgency = "caution";
    confidence = "medium";
    reason = "you're outside your trading window";
  }

  // Give-back + a still-decent day reads as protect, but if the target's been hit cleanly,
  // celebrate the discipline rather than warn.
  if (action === "trade" && input.dailyPnl >= input.dailyTarget) {
    action = "protect_green";
    urgency = "caution";
    reason = "you've hit your daily target — bank it or free-roll";
  }

  const narrative = createDisciplineNarrative(action, reason, factors);
  return { action, urgency, confidence, reason, narrative, factors, riskSignals };
}

// ── Trailing-drawdown helpers (the product's headline metric) ────────────────

/**
 * Dollars of room between current balance and the trailing floor.
 * On intraday-trailing accounts the floor follows the *peak* balance, so a floating
 * winner that round-trips still ratchets the floor up — hence peak, not current.
 */
export function cushionToFloor(params: {
  startingBalance: number;
  trailingDrawdown: number;
  currentBalance: number;
  peakBalance: number;
  drawdownType: "intraday" | "end_of_day";
}): number {
  const { startingBalance, trailingDrawdown, currentBalance, peakBalance, drawdownType } = params;
  const anchor = drawdownType === "intraday" ? Math.max(peakBalance, currentBalance) : currentBalance;
  const floor = anchor - trailingDrawdown;
  // Floor never trails below the starting balance minus the drawdown.
  const effectiveFloor = Math.max(floor, startingBalance - trailingDrawdown);
  return currentBalance - effectiveFloor;
}
