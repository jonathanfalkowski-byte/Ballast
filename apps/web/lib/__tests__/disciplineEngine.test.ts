import { describe, it, expect } from "vitest";
import {
  evaluateDiscipline,
  floorIsLocked,
  floorLevel,
  type DisciplineInput,
} from "../disciplineEngine";

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

describe("trailing drawdown floor", () => {
  it("does not let an EOD floor fall with an intraday loss", () => {
    expect(
      floorLevel({
        startingBalance: 250_000,
        trailingDrawdown: 6_500,
        currentBalance: 249_000,
        peakBalance: 255_000,
        endOfDayHighWater: 252_000,
        drawdownType: "end_of_day",
      }),
    ).toBe(245_500);
  });

  it("does not advance an EOD floor from an unrealized intraday winner", () => {
    expect(
      floorLevel({
        startingBalance: 250_000,
        trailingDrawdown: 6_500,
        currentBalance: 260_000,
        peakBalance: 260_000,
        endOfDayHighWater: 252_000,
        drawdownType: "end_of_day",
      }),
    ).toBe(245_500);
  });

  it("uses the persisted EOD anchor when deciding whether the floor locked", () => {
    expect(
      floorIsLocked({
        startingBalance: 250_000,
        trailingDrawdown: 6_500,
        currentBalance: 270_000,
        peakBalance: 270_000,
        endOfDayHighWater: 255_000,
        drawdownType: "end_of_day",
        lockFloorAt: 250_000,
      }),
    ).toBe(false);
  });
});
