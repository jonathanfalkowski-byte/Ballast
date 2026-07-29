"use client";

import { useMemo, useState } from "react";
import { evaluateDiscipline, cushionToFloor, type DisciplineInput } from "@/lib/disciplineEngine";

type Trade = { id: number; symbol: string; direction: "long" | "short"; contracts: number; pnl: number; tag: string };

const TAGS = ["a_plus", "plan", "revenge", "fomo", "boredom"];
const URGENCY_COLOR: Record<string, string> = { calm: "#3fb950", caution: "#e3b341", alert: "#f4523b" };

function money(n: number) {
  return (n < 0 ? "-$" : "$") + Math.abs(Math.round(n)).toLocaleString();
}

export default function SessionConsole() {
  // Account / rules (defaults mirror the migrations)
  const [accountSize] = useState(150000);
  const [trailing] = useState(5000);
  const [ddType, setDdType] = useState<"intraday" | "end_of_day">("intraday");
  const rules = { maxLossesBeforeStop: 2, dailyLossLimit: 500, dailyTarget: 500, maxTrades: 4, maxContracts: 1, cooldownMinutes: 5, sessionStartMinute: 570, sessionEndMinute: 690 };

  // Live inputs
  const [minutesSinceLastLoss, setMins] = useState(10);
  const [plannedContracts, setPlanned] = useState(1);
  const [nowMinuteEt, setNow] = useState(600); // 10:00 ET

  // Trade log (in-memory demo)
  const [trades, setTrades] = useState<Trade[]>([]);
  const [draft, setDraft] = useState<Trade>({ id: 0, symbol: "ES", direction: "long", contracts: 1, pnl: 0, tag: "plan" });
  // P&L kept as a string so a leading "-" can be typed (a number-coerced input drops it).
  const [pnlStr, setPnlStr] = useState("-200");

  // Derive session state from the log
  const dailyPnl = trades.reduce((s, t) => s + t.pnl, 0);
  const lossesToday = trades.filter((t) => t.pnl < 0).length;
  const tradesToday = trades.length;
  const lastTradeWasLoss = trades.length > 0 && trades[trades.length - 1].pnl < 0;
  const peakDailyPnl = useMemo(() => {
    let run = 0, peak = 0;
    for (const t of trades) { run += t.pnl; if (run > peak) peak = run; }
    return peak;
  }, [trades]);

  const currentBalance = accountSize + dailyPnl;
  const peakBalance = accountSize + peakDailyPnl;
  const cushion = cushionToFloor({ startingBalance: accountSize, trailingDrawdown: trailing, currentBalance, peakBalance, drawdownType: ddType });

  const input: DisciplineInput = {
    lossesToday, tradesToday, dailyPnl, peakDailyPnl,
    dailyLossLimit: rules.dailyLossLimit, dailyTarget: rules.dailyTarget,
    maxLossesBeforeStop: rules.maxLossesBeforeStop, maxTrades: rules.maxTrades,
    maxContracts: rules.maxContracts, plannedContracts,
    cushionToFloor: cushion, drawdownType: ddType,
    lastTradeWasLoss, minutesSinceLastLoss,
    cooldownMinutes: rules.cooldownMinutes, nowMinuteEt,
    sessionStartMinute: rules.sessionStartMinute, sessionEndMinute: rules.sessionEndMinute,
  };
  const decision = evaluateDiscipline(input);
  const color = URGENCY_COLOR[decision.urgency];

  function addTrade() {
    const pnl = parseFloat(pnlStr) || 0;
    setTrades((t) => [...t, { ...draft, pnl, id: Date.now() }]);
    setMins(0); // just traded — cooldown clock resets
  }
  function reset() { setTrades([]); setPnlStr("-200"); setMins(10); }

  return (
    <div className="space-y-6">
      {/* The next-action card — the product's core surface */}
      <div className="rounded-2xl border p-6" style={{ borderColor: color, background: "#12161c" }}>
        <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.12em]" style={{ color }}>
          <span>●</span> Next action · {decision.urgency}
        </div>
        <h2 className="mt-2 text-2xl font-extrabold tracking-tight">{decision.narrative.headline}</h2>
        {decision.narrative.bullets.length > 0 && (
          <ul className="mt-3 space-y-1.5">
            {decision.narrative.bullets.map((b, i) => (
              <li key={i} className="text-[15px] text-[#c7d1db]">• {b}</li>
            ))}
          </ul>
        )}
        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="Day P&L" value={money(dailyPnl)} tone={dailyPnl >= 0 ? "g" : "r"} />
          <Stat label="Losses" value={`${lossesToday} / ${rules.maxLossesBeforeStop}`} tone={lossesToday >= rules.maxLossesBeforeStop ? "r" : "n"} />
          <Stat label="Trades" value={`${tradesToday} / ${rules.maxTrades}`} tone="n" />
          <Stat label="Cushion to floor" value={money(cushion)} tone={cushion < trailing * 0.3 ? "r" : "g"} />
        </div>
      </div>

      {/* Live controls */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Ctl label="Drawdown type">
          <select value={ddType} onChange={(e) => setDdType(e.target.value as "intraday" | "end_of_day")} className={inputCls}>
            <option value="intraday">Intraday trailing</option>
            <option value="end_of_day">End-of-day trailing</option>
          </select>
        </Ctl>
        <Ctl label={`Minutes since last loss: ${minutesSinceLastLoss}`}>
          <input type="range" min={0} max={30} value={minutesSinceLastLoss} onChange={(e) => setMins(+e.target.value)} className="w-full accent-[#4da3ff]" />
        </Ctl>
        <Ctl label={`Now (ET): ${Math.floor(nowMinuteEt / 60)}:${String(nowMinuteEt % 60).padStart(2, "0")}`}>
          <input type="range" min={540} max={780} value={nowMinuteEt} onChange={(e) => setNow(+e.target.value)} className="w-full accent-[#4da3ff]" />
        </Ctl>
      </div>

      {/* Trade entry */}
      <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
        <h3 className="mb-3 text-[16px] font-semibold">Log a trade</h3>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
          <input value={draft.symbol} onChange={(e) => setDraft({ ...draft, symbol: e.target.value })} placeholder="Symbol" className={inputCls} />
          <select value={draft.direction} onChange={(e) => setDraft({ ...draft, direction: e.target.value as "long" | "short" })} className={inputCls}>
            <option value="long">Long</option><option value="short">Short</option>
          </select>
          <input type="number" value={draft.contracts} onChange={(e) => setDraft({ ...draft, contracts: +e.target.value })} placeholder="Contracts" className={inputCls} />
          <input inputMode="text" value={pnlStr} onChange={(e) => setPnlStr(e.target.value)} placeholder="P&L $ (e.g. -200)" className={inputCls} />
          <select value={draft.tag} onChange={(e) => setDraft({ ...draft, tag: e.target.value })} className={inputCls}>
            {TAGS.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>
        <div className="mt-3 flex gap-2">
          <button onClick={addTrade} className="rounded-lg bg-[#4da3ff] px-4 py-2 text-[14px] font-semibold text-[#04121f]">Add trade</button>
          <button onClick={reset} className="rounded-lg border border-[#2a333f] px-4 py-2 text-[14px] text-[#9aa7b4]">Reset day</button>
        </div>

        {trades.length > 0 && (
          <div className="mt-4 space-y-1">
            {trades.map((t) => (
              <div key={t.id} className="flex items-center justify-between rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2 text-[14px]">
                <span>{t.direction === "long" ? "▲" : "▼"} {t.symbol} ×{t.contracts} <span className="ml-2 rounded bg-[#20303f] px-2 py-0.5 text-[12px] text-[#bcd6ef]">{t.tag}</span></span>
                <span className={t.pnl >= 0 ? "text-[#3fb950]" : "text-[#f4523b]"}>{money(t.pnl)}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      <p className="text-[13px] text-[#7f8b98]">
        This is a live, in-memory demo of the engine — add a couple of losing trades and watch the next-action
        card flip to <span className="text-[#f4523b]">Stop</span>. In the real app this state comes from your
        logged trades and account, and the card fires in the moment.
      </p>
    </div>
  );
}

const inputCls = "rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]";

function Stat({ label, value, tone }: { label: string; value: string; tone: "g" | "r" | "n" }) {
  const c = tone === "g" ? "#3fb950" : tone === "r" ? "#f4523b" : "#e8edf3";
  return (
    <div className="rounded-lg border border-[#2a333f] bg-[#0e141b] p-3">
      <div className="text-[12px] text-[#9aa7b4]">{label}</div>
      <div className="text-[18px] font-bold" style={{ color: c }}>{value}</div>
    </div>
  );
}
function Ctl({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-4">
      <label className="mb-2 block text-[13px] text-[#9aa7b4]">{label}</label>
      {children}
    </div>
  );
}
