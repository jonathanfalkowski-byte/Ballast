import { describe, it, expect } from "vitest";
import { evaluateDiscipline, type DisciplineInput } from "../disciplineEngine";

const base: DisciplineInput = {
  lossesToday: 0,
  tradesToday: 1,
  dailyPnl: 100,
  peakDailyPnl: 100,
  dailyLossLimit: 500,
  dailyTarget: 500,
  maxLossesBeforeStop: 2,
  maxTrades: 4,
  maxContracts: 1,
  plannedContracts: 1,
  cushionToFloor: 5000,
  drawdownType: "intraday",
  lastTradeWasLoss: false,
  minutesSinceLastLoss: null,
  cooldownMinutes: 5,
  nowMinuteEt: 600,
  sessionStartMinute: 570,
  sessionEndMinute: 690,
};

describe("disciplineEngine", () => {
  it("clears a clean setup to trade", () => {
    expect(evaluateDiscipline(base).action).toBe("trade");
  });
  it("stops after the max losses", () => {
    expect(evaluateDiscipline({ ...base, lossesToday: 2 }).action).toBe("stop_for_day");
  });
  it("locks out at the daily loss limit", () => {
    expect(evaluateDiscipline({ ...base, dailyPnl: -500 }).action).toBe("lockout");
  });
  it("catches the revenge window", () => {
    const d = evaluateDiscipline({ ...base, lastTradeWasLoss: true, minutesSinceLastLoss: 2 });
    expect(d.action).toBe("cooldown");
    expect(d.urgency).toBe("alert");
  });
  it("sizes down on a thin cushion", () => {
    expect(evaluateDiscipline({ ...base, cushionToFloor: 300 }).action).toBe("size_down");
  });
  it("protects a green day at target", () => {
    expect(evaluateDiscipline({ ...base, dailyPnl: 600, peakDailyPnl: 600 }).action).toBe("protect_green");
  });
});
