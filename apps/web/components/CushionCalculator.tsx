"use client";

import { useMemo, useState } from "react";
import { cushionToFloor } from "@/lib/disciplineEngine";

const POINT_VALUE: Record<string, number> = { ES: 50, NQ: 20, MES: 5, MNQ: 2 };

function money(n: number) {
  return "$" + Math.round(n).toLocaleString();
}

export default function CushionCalculator() {
  // Trailing drawdown
  const [startingBalance, setStart] = useState(150000);
  const [trailing, setTrailing] = useState(5000);
  const [current, setCurrent] = useState(151200);
  const [peak, setPeak] = useState(152000);
  const [ddType, setDdType] = useState<"intraday" | "end_of_day">("intraday");

  // Position size
  const [inst, setInst] = useState("ES");
  const [riskPerTrade, setRisk] = useState(200);
  const [stopPoints, setStop] = useState(5);

  const cushion = useMemo(
    () =>
      cushionToFloor({
        startingBalance,
        trailingDrawdown: trailing,
        currentBalance: current,
        peakBalance: peak,
        drawdownType: ddType,
      }),
    [startingBalance, trailing, current, peak, ddType],
  );

  const pv = POINT_VALUE[inst];
  const riskPerContract = stopPoints * pv;
  const contracts = riskPerContract > 0 ? Math.floor(riskPerTrade / riskPerContract) : 0;
  const actualRisk = contracts * riskPerContract;
  const cushionAfterStop = cushion - actualRisk;

  return (
    <div className="space-y-6">
      {/* Trailing drawdown */}
      <Card title="Trailing-drawdown cushion">
        <Grid>
          <Num label="Starting balance ($)" value={startingBalance} onChange={setStart} />
          <Num label="Trailing drawdown ($)" value={trailing} onChange={setTrailing} />
          <Num label="Current balance ($)" value={current} onChange={setCurrent} />
          <Num label="Peak balance ($)" value={peak} onChange={setPeak} />
          <div className="flex flex-col gap-1">
            <label className="text-[13px] text-[#9aa7b4]">Drawdown type</label>
            <select
              value={ddType}
              onChange={(e) => setDdType(e.target.value as "intraday" | "end_of_day")}
              className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
            >
              <option value="intraday">Intraday trailing</option>
              <option value="end_of_day">End-of-day trailing</option>
            </select>
          </div>
        </Grid>
        <Out>
          <div className="text-sm text-[#9aa7b4]">Room to your trailing floor</div>
          <div className={`text-3xl font-extrabold ${cushion < trailing * 0.3 ? "text-[#f4523b]" : "text-[#3fb950]"}`}>
            {money(cushion)}
          </div>
          {ddType === "intraday" && (
            <p className="mt-2 text-[13px] text-[#9aa7b4]">
              Intraday trailing follows your peak — a floating winner that round-trips still ratchets
              this floor up. Bank profit; don't admire it.
            </p>
          )}
        </Out>
      </Card>

      {/* Position size */}
      <Card title="Position size">
        <Grid>
          <div className="flex flex-col gap-1">
            <label className="text-[13px] text-[#9aa7b4]">Instrument</label>
            <select
              value={inst}
              onChange={(e) => setInst(e.target.value)}
              className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
            >
              <option value="ES">ES — S&P ($50/pt)</option>
              <option value="NQ">NQ — Nasdaq ($20/pt)</option>
              <option value="MES">MES — Micro S&P ($5/pt)</option>
              <option value="MNQ">MNQ — Micro Nasdaq ($2/pt)</option>
            </select>
          </div>
          <Num label="Max risk / trade ($)" value={riskPerTrade} onChange={setRisk} />
          <Num label="Stop size (points)" value={stopPoints} onChange={setStop} step={0.25} />
        </Grid>
        <Out>
          <div className="flex items-end justify-between">
            <div>
              <div className="text-sm text-[#9aa7b4]">Contracts</div>
              <div className={`text-3xl font-extrabold ${contracts < 1 ? "text-[#f4523b]" : "text-[#3fb950]"}`}>
                {contracts}
              </div>
            </div>
            <div className="text-right text-sm text-[#9aa7b4]">
              <div>Actual risk: <b className="text-[#e8edf3]">{money(actualRisk)}</b></div>
              <div>
                Cushion after a full stop:{" "}
                <b className={cushionAfterStop < 0 ? "text-[#f4523b]" : "text-[#e8edf3]"}>
                  {money(cushionAfterStop)}
                </b>
              </div>
            </div>
          </div>
          {contracts < 1 && (
            <p className="mt-2 text-[13px] text-[#f4523b]">
              One contract risks {money(riskPerContract)} — more than your max. Use a micro or a
              tighter stop.
            </p>
          )}
          {contracts >= 1 && cushionAfterStop < 0 && (
            <p className="mt-2 text-[13px] text-[#f4523b]">
              A full stop here would breach your trailing floor. Do not take this trade at this size.
            </p>
          )}
        </Out>
      </Card>
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[#2a333f] bg-[#1c232d] p-5">
      <h3 className="mb-3 text-[17px] font-semibold">{title}</h3>
      {children}
    </div>
  );
}
function Grid({ children }: { children: React.ReactNode }) {
  return <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>;
}
function Out({ children }: { children: React.ReactNode }) {
  return <div className="mt-4 rounded-lg border border-[#2a333f] bg-[#0e141b] p-4">{children}</div>;
}
function Num({
  label,
  value,
  onChange,
  step,
}: {
  label: string;
  value: number;
  onChange: (n: number) => void;
  step?: number;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-[13px] text-[#9aa7b4]">{label}</label>
      <input
        type="number"
        value={value}
        step={step ?? 1}
        onChange={(e) => onChange(parseFloat(e.target.value) || 0)}
        className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
      />
    </div>
  );
}
