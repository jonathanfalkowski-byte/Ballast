// Trading discipline risk signals.
// Mirrors the shape of Velocity's riskSignals.ts, re-skinned from insurance to trading.

export type RiskSignalSeverity = "low" | "medium" | "high";

export type RiskSignalKey =
  | "loss_streak" // hit the max-losses circuit breaker
  | "daily_loss_limit" // at/through the hard daily loss limit
  | "revenge_window" // acting within the cooldown after a loss
  | "thin_cushion" // little room left to the trailing-drawdown floor
  | "give_back" // handing back a green day
  | "over_size" // planning more contracts than allowed
  | "over_trading" // past the max-trades count
  | "out_of_window"; // outside the trading session

export type RiskSignal = {
  key: RiskSignalKey;
  severity: RiskSignalSeverity;
  summary: string;
  facts?: string[];
};

export type DetectRiskSignalsInput = {
  lossesToday: number;
  tradesToday: number;
  dailyPnl: number;
  peakDailyPnl: number;
  dailyLossLimit: number;
  dailyTarget: number;
  maxLossesBeforeStop: number;
  maxTrades: number;
  maxContracts: number;
  plannedContracts: number;
  cushionToFloor: number; // dollars of room before the trailing floor
  drawdownType: "intraday" | "end_of_day";
  lastTradeWasLoss: boolean;
  minutesSinceLastLoss: number | null;
  cooldownMinutes: number;
  nowMinuteEt: number; // minutes since midnight ET
  sessionStartMinute: number;
  sessionEndMinute: number;
};

function money(n: number): string {
  return "$" + Math.round(n).toLocaleString();
}

export function detectRiskSignals(input: DetectRiskSignalsInput): RiskSignal[] {
  const signals: RiskSignal[] = [];

  if (input.lossesToday >= input.maxLossesBeforeStop) {
    signals.push({
      key: "loss_streak",
      severity: "high",
      summary: `You've taken ${input.lossesToday} losses — your stop-after-${input.maxLossesBeforeStop} line.`,
    });
  }

  if (input.dailyPnl <= -Math.abs(input.dailyLossLimit)) {
    signals.push({
      key: "daily_loss_limit",
      severity: "high",
      summary: `Down ${money(-input.dailyPnl)} — at or past your ${money(input.dailyLossLimit)} daily limit.`,
    });
  }

  if (
    input.lastTradeWasLoss &&
    input.minutesSinceLastLoss !== null &&
    input.minutesSinceLastLoss < input.cooldownMinutes
  ) {
    signals.push({
      key: "revenge_window",
      severity: "high",
      summary: `Only ${input.minutesSinceLastLoss} min since a loss — inside the ${input.cooldownMinutes}-min cooldown. This is where revenge trades live.`,
    });
  }

  // Thin cushion matters most on intraday-trailing accounts, where floating gains ratchet the floor.
  const cushionThreshold = Math.max(input.dailyLossLimit, 400);
  if (input.cushionToFloor > 0 && input.cushionToFloor < cushionThreshold) {
    signals.push({
      key: "thin_cushion",
      severity: input.drawdownType === "intraday" ? "high" : "medium",
      summary: `Only ${money(input.cushionToFloor)} to your trailing floor. One full stop could end the account.`,
    });
  }

  if (
    input.peakDailyPnl >= input.dailyTarget &&
    input.dailyPnl <= input.peakDailyPnl * 0.6
  ) {
    signals.push({
      key: "give_back",
      severity: "high",
      summary: `You were up ${money(input.peakDailyPnl)} and have handed back ${money(input.peakDailyPnl - input.dailyPnl)}. Protect the green.`,
    });
  }

  if (input.plannedContracts > input.maxContracts) {
    signals.push({
      key: "over_size",
      severity: "medium",
      summary: `Planning ${input.plannedContracts} contracts — over your ${input.maxContracts} cap. Sizing up is how the blowup starts.`,
    });
  }

  if (input.tradesToday >= input.maxTrades) {
    signals.push({
      key: "over_trading",
      severity: "medium",
      summary: `That's ${input.tradesToday} trades — at your max of ${input.maxTrades}.`,
    });
  }

  if (
    input.nowMinuteEt < input.sessionStartMinute ||
    input.nowMinuteEt > input.sessionEndMinute
  ) {
    signals.push({
      key: "out_of_window",
      severity: "low",
      summary: "Outside your 9:30–11:30 ET window. This is where afternoon revenge trades happen.",
    });
  }

  return signals;
}
