"use client";

import { useMemo, useState } from "react";
import { edgeByTag, behaviourCost, money, TAG_LABEL, EMOTIONAL_TAGS, type Tag } from "@/lib/analytics";

const inputCls =
  "rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff] w-full";

const SAMPLE = `220,plan
-150,plan
180,a_plus
-420,revenge
220,plan
-150,plan
-420,revenge
130,revenge
-260,fomo
220,plan`;

type Row = { pnl: number; tag: string };

// Accepts "pnl,tag" per line (header optional). Tags are matched loosely.
function parseRows(raw: string): Row[] {
  const out: Row[] = [];
  for (const line of raw.split(/\r?\n/)) {
    const l = line.trim();
    if (!l) continue;
    const cells = l.split(/[,\t;]/).map((c) => c.trim());
    if (cells.length < 2) continue;

    // Find the numeric cell and the tag cell, whichever order they're in.
    let pnl: number | null = null;
    let tag = "untagged";
    for (const c of cells) {
      let v = c;
      let neg = false;
      if (/^\(.*\)$/.test(v)) { neg = true; v = v.slice(1, -1); }
      v = v.replace(/[$,\s]/g, "");
      const num = parseFloat(v);
      if (!isNaN(num) && /^-?[\d.]+$/.test(v)) {
        if (pnl === null) pnl = neg ? -Math.abs(num) : num;
      } else if (c) {
        const norm = c.toLowerCase().replace(/[^a-z+]/g, "");
        if (/^a\+?$|aplus|^a$/.test(norm)) tag = "a_plus";
        else if (/plan/.test(norm)) tag = "plan";
        else if (/reveng|tilt/.test(norm)) tag = "revenge";
        else if (/fomo|chase/.test(norm)) tag = "fomo";
        else if (/bored/.test(norm)) tag = "boredom";
      }
    }
    if (pnl !== null) out.push({ pnl, tag });
  }
  return out;
}

export default function InsightsTool() {
  const [raw, setRaw] = useState("");
  const rows = useMemo(() => parseRows(raw), [raw]);
  const tagged = rows.length > 0;
  const edges = useMemo(() => (tagged ? edgeByTag(rows) : []), [rows, tagged]);
  const cost = useMemo(() => (tagged ? behaviourCost(rows) : null), [rows, tagged]);

  const maxAbs = Math.max(1, ...edges.map((e) => Math.abs(e.expectancy)));

  return (
    <div className="space-y-6">
      <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
        <h3 className="mb-1 text-[16px] font-semibold">Paste your trades with a tag</h3>
        <p className="mb-3 text-[13px] text-[#9aa7b4]">
          One per line as <code className="text-[#bcd6ef]">pnl,tag</code> — e.g.{" "}
          <code className="text-[#bcd6ef]">-420,revenge</code>. Tags:{" "}
          {(Object.keys(TAG_LABEL) as Tag[]).map((t) => (
            <code key={t} className="mr-1 text-[#bcd6ef]">{t}</code>
          ))}
        </p>
        <textarea
          value={raw}
          onChange={(e) => setRaw(e.target.value)}
          rows={7}
          placeholder={SAMPLE}
          className={inputCls + " font-mono text-[14px]"}
        />
        <div className="mt-2 flex items-center justify-between">
          <button onClick={() => setRaw(SAMPLE)} className="text-[13px] text-[#4da3ff] hover:underline">
            load sample data
          </button>
          <span className="text-[13px] text-[#7f8b98]">{rows.length} trades</span>
        </div>
      </div>

      {cost && rows.length > 0 && (
        <>
          {/* The headline nobody else shows you */}
          <div
            className="rounded-2xl border p-6"
            style={{
              borderColor: cost.costOfEmotionalTrading > 0 ? "#f4523b" : "#3fb950",
              background: "#12161c",
            }}
          >
            <div
              className="text-xs font-bold uppercase tracking-[0.12em]"
              style={{ color: cost.costOfEmotionalTrading > 0 ? "#f4523b" : "#3fb950" }}
            >
              What undisciplined trades cost you
            </div>
            {cost.costOfEmotionalTrading > 0 ? (
              <>
                <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-[#f4523b]">
                  {money(cost.costOfEmotionalTrading)}
                </h2>
                <p className="mt-2 text-[15px] text-[#c7d1db]">
                  Across {cost.emotionalTrades} revenge / FOMO / boredom trades. You finished at{" "}
                  <b className={cost.actualPnl >= 0 ? "text-[#3fb950]" : "text-[#f4523b]"}>
                    {money(cost.actualPnl)}
                  </b>
                  . Without those trades you&apos;d be at{" "}
                  <b className="text-[#3fb950]">{money(cost.pnlWithoutEmotional)}</b>.
                </p>
              </>
            ) : (
              <>
                <h2 className="mt-2 text-2xl font-extrabold tracking-tight text-[#3fb950]">
                  Nothing — your tagged emotional trades didn&apos;t lose money.
                </h2>
                <p className="mt-2 text-[15px] text-[#c7d1db]">
                  Rare. Keep tagging honestly; this number tends to appear over a longer sample.
                </p>
              </>
            )}
          </div>

          {/* Edge by tag */}
          <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
            <h3 className="mb-1 text-[16px] font-semibold">Your edge, by behaviour</h3>
            <p className="mb-4 text-[13px] text-[#9aa7b4]">
              Worst first. The average across all trades hides this — which is the point.
            </p>
            <div className="space-y-3">
              {edges.map((e) => {
                const bad = e.expectancy < 0;
                const color = bad ? "#f4523b" : "#3fb950";
                const width = (Math.abs(e.expectancy) / maxAbs) * 100;
                const emotional = EMOTIONAL_TAGS.includes(e.tag as Tag);
                return (
                  <div key={e.tag}>
                    <div className="flex items-baseline justify-between text-[14px]">
                      <span className="font-semibold">
                        {e.label}
                        {emotional && (
                          <span className="ml-2 rounded bg-[#2e1a1a] px-1.5 py-0.5 text-[11px] text-[#f4523b]">
                            emotional
                          </span>
                        )}
                      </span>
                      <span style={{ color }} className="font-bold tabular-nums">
                        {money(e.expectancy)}/trade
                      </span>
                    </div>
                    <div className="mt-1 h-2 w-full overflow-hidden rounded bg-[#0e141b]">
                      <div className="h-full rounded" style={{ width: `${width}%`, background: color }} />
                    </div>
                    <div className="mt-1 text-[12px] text-[#7f8b98]">
                      {e.n} trades · {(e.winRate * 100).toFixed(0)}% win · total{" "}
                      <span style={{ color }}>{money(e.total)}</span>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </>
      )}

      <p className="text-[12px] text-[#7f8b98]">
        Analysis of the data you paste; not financial advice and not a prediction. Tagging honestly is
        the whole point — a tag you fudge is a lesson you don&apos;t get.
      </p>
    </div>
  );
}
