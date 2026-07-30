"use client";

import { useState } from "react";
import {
  parsePnls,
  computeStats,
  monteCarlo,
  verdict,
  money,
  type AccountRules,
  type TradeStats,
  type MonteCarloResult,
  type Verdict,
} from "@/lib/readiness";

type Preset = { label: string; rules: Omit<AccountRules, "maxTrades"> };

// Approximate, illustrative presets — rules change often, so ALWAYS verify with the firm.
const PRESETS: Record<string, Preset> = {
  custom: { label: "Custom", rules: { trailingDrawdown: 2500, profitTarget: 3000, dailyLossLimit: 0, tradesPerDay: 4 } },
  apex50: { label: "Apex 50K (approx)", rules: { trailingDrawdown: 2500, profitTarget: 3000, dailyLossLimit: 0, tradesPerDay: 4 } },
  apex100: { label: "Apex 100K (approx)", rules: { trailingDrawdown: 3000, profitTarget: 6000, dailyLossLimit: 0, tradesPerDay: 4 } },
  apex150: { label: "Apex 150K (approx)", rules: { trailingDrawdown: 5000, profitTarget: 9000, dailyLossLimit: 0, tradesPerDay: 4 } },
  topstep50: { label: "Topstep 50K (approx)", rules: { trailingDrawdown: 2000, profitTarget: 3000, dailyLossLimit: 1000, tradesPerDay: 4 } },
  topstep100: { label: "Topstep 100K (approx)", rules: { trailingDrawdown: 3000, profitTarget: 6000, dailyLossLimit: 2000, tradesPerDay: 4 } },
};

const VERDICT_COLOR: Record<Verdict["level"], string> = {
  ready: "#3fb950",
  borderline: "#e3b341",
  not_ready: "#f4523b",
  no_edge: "#f4523b",
  not_enough_data: "#4da3ff",
};

const inputCls = "rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff] w-full";

export default function ReadinessTool() {
  const [raw, setRaw] = useState("");
  const [presetKey, setPresetKey] = useState("custom");
  const [dd, setDd] = useState(2500);
  const [target, setTarget] = useState(3000);
  const [daily, setDaily] = useState(0);
  const [tpd, setTpd] = useState(4);
  const [result, setResult] = useState<{ stats: TradeStats; mc: MonteCarloResult; v: Verdict } | null>(null);
  const [running, setRunning] = useState(false);

  function applyPreset(key: string) {
    setPresetKey(key);
    const p = PRESETS[key];
    if (p && key !== "custom") {
      setDd(p.rules.trailingDrawdown);
      setTarget(p.rules.profitTarget);
      setDaily(p.rules.dailyLossLimit);
      setTpd(p.rules.tradesPerDay);
    }
  }

  function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => setRaw(String(reader.result || ""));
    reader.readAsText(file);
  }

  function assess() {
    if (running) return;
    setRunning(true);
    setResult(null);
    // Yield to the browser first so the "Running…" state paints before the
    // simulation loop occupies the main thread (otherwise the page appears frozen).
    setTimeout(() => {
      try {
        const pnls = parsePnls(raw);
        const rules: AccountRules = {
          trailingDrawdown: dd,
          profitTarget: target,
          dailyLossLimit: daily,
          tradesPerDay: tpd,
          maxTrades: 500,
        };
        const stats = computeStats(pnls);
        const mc = monteCarlo(pnls, rules, 5000);
        setResult({ stats, mc, v: verdict(stats, mc) });
      } finally {
        setRunning(false);
      }
    }, 50);
  }

  const parsedCount = parsePnls(raw).length;

  return (
    <div className="space-y-6">
      {/* Input */}
      <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
        <h3 className="mb-1 text-[16px] font-semibold">1. Paste your trades</h3>
        <p className="mb-3 text-[13px] text-[#9aa7b4]">
          One P&amp;L per line (e.g. <code className="text-[#bcd6ef]">250</code> then <code className="text-[#bcd6ef]">-150</code>), or paste/upload a CSV export. Aim for 30+ trades — more is better.
        </p>
        <textarea
          value={raw}
          onChange={(e) => setRaw(e.target.value)}
          rows={6}
          placeholder={"250\n-150\n420\n-200\n..."}
          className={inputCls + " font-mono text-[14px]"}
        />
        <div className="mt-2 flex items-center justify-between">
          <label className="text-[13px] text-[#4da3ff] cursor-pointer hover:underline">
            or upload a CSV
            <input type="file" accept=".csv,.txt,text/csv,text/plain" onChange={onFile} className="hidden" />
          </label>
          <span className="text-[13px] text-[#7f8b98]">{parsedCount} trades detected</span>
        </div>
      </div>

      {/* Rules */}
      <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
        <h3 className="mb-1 text-[16px] font-semibold">2. Your account rules</h3>
        <p className="mb-3 text-[13px] text-[#9aa7b4]">Presets are approximate — confirm the exact numbers with your firm, since they change.</p>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className="flex flex-col gap-1 sm:col-span-2">
            <label className="text-[13px] text-[#9aa7b4]">Firm / account preset</label>
            <select value={presetKey} onChange={(e) => applyPreset(e.target.value)} className={inputCls}>
              {Object.entries(PRESETS).map(([k, p]) => <option key={k} value={k}>{p.label}</option>)}
            </select>
          </div>
          <Field label="Trailing drawdown ($)" value={dd} onChange={(v) => { setDd(v); setPresetKey("custom"); }} />
          <Field label="Profit target to pass ($)" value={target} onChange={(v) => { setTarget(v); setPresetKey("custom"); }} />
          <Field label="Daily loss limit ($, 0 = none)" value={daily} onChange={(v) => { setDaily(v); setPresetKey("custom"); }} />
          <Field label="Typical trades per day" value={tpd} onChange={(v) => { setTpd(v); setPresetKey("custom"); }} />
        </div>
        <button
          onClick={assess}
          disabled={running || parsedCount === 0}
          className="mt-4 rounded-lg bg-[#3fb950] px-5 py-2.5 text-[15px] font-semibold text-[#08240f] disabled:opacity-60"
        >
          {running ? "Running 5,000 simulations…" : "Assess my readiness"}
        </button>
      </div>

      {/* Result */}
      {result && (
        <div className="space-y-4">
          <div className="rounded-2xl border p-6" style={{ borderColor: VERDICT_COLOR[result.v.level], background: "#12161c" }}>
            <div className="text-xs font-bold uppercase tracking-[0.12em]" style={{ color: VERDICT_COLOR[result.v.level] }}>
              Readiness verdict
            </div>
            <h2 className="mt-2 text-2xl font-extrabold tracking-tight">{result.v.headline}</h2>
            <p className="mt-2 text-[15px] text-[#c7d1db]">{result.v.detail}</p>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Stat label="Trades" value={String(result.stats.n)} />
            <Stat label="Win rate" value={(result.stats.winRate * 100).toFixed(0) + "%"} />
            <Stat label="Expectancy / trade" value={money(result.stats.expectancy)} tone={result.stats.expectancy > 0 ? "g" : "r"} />
            <Stat label="Avg win" value={money(result.stats.avgWin)} tone="g" />
            <Stat label="Avg loss" value={money(result.stats.avgLoss)} tone="r" />
            <Stat label="Profit factor" value={isFinite(result.stats.profitFactor) ? result.stats.profitFactor.toFixed(2) : "∞"} />
          </div>

          <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
            <div className="text-[13px] text-[#9aa7b4]">Rule-aware Monte Carlo ({result.mc.runs.toLocaleString()} simulated attempts, drawing from your own trades)</div>
            <div className="mt-2 flex items-end gap-3">
              <div className="text-4xl font-extrabold" style={{ color: VERDICT_COLOR[result.v.level] }}>
                {(result.mc.pSuccess * 100).toFixed(0)}%
              </div>
              <div className="pb-1 text-[14px] text-[#9aa7b4]">reach the target before breaching a rule</div>
            </div>
            <div className="mt-3 grid grid-cols-2 gap-2 text-[13px] text-[#9aa7b4] sm:grid-cols-3">
              <div>Drawdown breach: <b className="text-[#f4523b]">{(result.mc.pDrawdownBreach * 100).toFixed(0)}%</b></div>
              {daily > 0 && <div>Daily-loss breach: <b className="text-[#f4523b]">{(result.mc.pDailyBreach * 100).toFixed(0)}%</b></div>}
              <div>Median trades to outcome: <b className="text-[#e8edf3]">{result.mc.medianTradesToOutcome}</b></div>
            </div>
          </div>
        </div>
      )}

      <p className="text-[12px] text-[#7f8b98]">
        Illustrative estimate only, not a prediction and not financial advice. It assumes your future trades resemble the ones you pasted (rarely perfectly true), models trailing drawdown from peak balance, and ignores fees, slippage, commissions, consistency rules, and payout mechanics. Always reverify your firm&apos;s current rules.
      </p>
    </div>
  );
}

function Field({ label, value, onChange }: { label: string; value: number; onChange: (n: number) => void }) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-[13px] text-[#9aa7b4]">{label}</label>
      <input type="number" value={value} onChange={(e) => onChange(parseFloat(e.target.value) || 0)} className={inputCls} />
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: "g" | "r" }) {
  const c = tone === "g" ? "#3fb950" : tone === "r" ? "#f4523b" : "#e8edf3";
  return (
    <div className="rounded-lg border border-[#2a333f] bg-[#0e141b] p-3">
      <div className="text-[12px] text-[#9aa7b4]">{label}</div>
      <div className="text-[18px] font-bold" style={{ color: c }}>{value}</div>
    </div>
  );
}
