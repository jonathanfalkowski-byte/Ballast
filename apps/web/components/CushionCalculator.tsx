"use client";

import { useMemo, useState } from "react";
import {
  cushionToFloor,
  floorLevel,
  floorIsLocked,
  peakThatLocks,
} from "@/lib/disciplineEngine";

const POINT_VALUE: Record<string, number> = { ES: 50, NQ: 20, MES: 5, MNQ: 2 };

type Kind = "eval_rithmic" | "eval_tradovate" | "funded" | "own";

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

  // What kind of account, which is what decides whether the floor ever stops.
  //
  // The calculator used to ignore this entirely, so it silently gave the
  // trail-forever answer for every account - wrong for every funded account,
  // and wrong for every Apex evaluation on Rithmic or WealthCharts. On a 250K
  // legacy eval that is the difference between $6,500 of room and $15,000.
  const [kind, setKind] = useState<Kind>("eval_rithmic");
  const [target, setTarget] = useState(9000);

  const lockFloorAt = useMemo(() => {
    if (kind === "eval_tradovate") return 0;                    // never stops
    if (kind === "funded") return startingBalance + 100;        // Apex-style lock
    if (kind === "eval_rithmic") return startingBalance + target; // target profit balance
    if (kind === "own") return startingBalance - trailing;      // fixed from the start
    return 0;
  }, [kind, startingBalance, target, trailing]);

  // Position size
  const [inst, setInst] = useState("ES");
  const [riskPerTrade, setRisk] = useState(200);
  const [stopPoints, setStop] = useState(5);

  const floorParams = useMemo(
    () => ({
      startingBalance,
      trailingDrawdown: trailing,
      currentBalance: current,
      peakBalance: peak,
      drawdownType: ddType,
      lockFloorAt,
    }),
    [startingBalance, trailing, current, peak, ddType, lockFloorAt],
  );

  const cushion = useMemo(() => cushionToFloor(floorParams), [floorParams]);
  const floor = useMemo(() => floorLevel(floorParams), [floorParams]);
  const locked = useMemo(() => floorIsLocked(floorParams), [floorParams]);
  const locksAtPeak = useMemo(() => peakThatLocks(floorParams), [floorParams]);

  // What a trader most wants to know and no calculator tells them: how much
  // further the peak has to go before profit starts being worth something.
  const toGo = locksAtPeak > 0 ? Math.max(0, locksAtPeak - Math.max(peak, current)) : 0;

  const pv = POINT_VALUE[inst];
  const riskPerContract = stopPoints * pv;
  const contracts = riskPerContract > 0 ? Math.floor(riskPerTrade / riskPerContract) : 0;
  const actualRisk = contracts * riskPerContract;
  const cushionAfterStop = cushion - actualRisk;

  // The number that actually matters: risk as a share of the REMAINING FAILURE BUFFER,
  // not of the advertised account size. A $1,000 stop on a "$100k" account with a $3k
  // trailing drawdown is not 1% risk — it's ~33% of everything standing between you
  // and a blown account.
  const bufferPct = cushion > 0 ? (actualRisk / cushion) * 100 : Infinity;
  const accountPct = startingBalance > 0 ? (actualRisk / startingBalance) * 100 : 0;
  const fullStopsLeft = actualRisk > 0 ? Math.floor(cushion / actualRisk) : Infinity;
  const bufferTone = bufferPct >= 33 ? "#f4523b" : bufferPct >= 15 ? "#e3b341" : "#3fb950";

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
          <div className="flex flex-col gap-1">
            <label className="text-[13px] text-[#9aa7b4]">What kind of account</label>
            <select
              value={kind}
              onChange={(e) => setKind(e.target.value as Kind)}
              className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
            >
              <option value="eval_rithmic">Evaluation &mdash; Rithmic / WealthCharts</option>
              <option value="eval_tradovate">Evaluation &mdash; Tradovate</option>
              <option value="funded">Funded / PA</option>
              <option value="own">My own money (fixed max loss)</option>
            </select>
          </div>
          {kind === "eval_rithmic" && (
            <Num label="Profit target to pass ($)" value={target} onChange={setTarget} />
          )}
        </Grid>
        <Out>
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <div className="text-sm text-[#9aa7b4]">Room to your floor</div>
              <div
                className={`text-3xl font-extrabold ${
                  cushion < trailing * 0.3 ? "text-[#f4523b]" : "text-[#3fb950]"
                }`}
              >
                {money(cushion)}
              </div>
            </div>
            <div className="text-right text-sm text-[#9aa7b4]">
              <div>
                Your account dies at <b className="text-[#e8edf3]">{money(floor)}</b>
              </div>
              <div className="mt-1">
                {lockFloorAt <= 0 ? (
                  <span className="text-[#e3b341]">This floor never stops following you</span>
                ) : locked ? (
                  <span className="text-[#3fb950]">
                    Locked at {money(lockFloorAt)} &mdash; profit above it is real cushion
                  </span>
                ) : (
                  <span>
                    Locks at {money(lockFloorAt)} once your peak reaches{" "}
                    <b className="text-[#e8edf3]">{money(locksAtPeak)}</b>
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* The bit nobody explains */}
          {lockFloorAt > 0 && !locked && (
            <p className="mt-3 text-[13px] text-[#9aa7b4]">
              Until then, <b className="text-[#e8edf3]">making money buys you no extra room</b> — the
              floor climbs with you and your cushion stays at {money(trailing)}. You need another{" "}
              <b className="text-[#e8edf3]">{money(toGo)}</b> of peak before that changes.
            </p>
          )}

          {lockFloorAt > 0 && locked && (
            <p className="mt-3 text-[13px] text-[#9aa7b4]">
              Your floor has stopped moving. From here every dollar you make is genuine cushion,
              which is why this is the point the whole evaluation turns on.
            </p>
          )}

          {lockFloorAt <= 0 && (
            <p className="mt-3 text-[13px] text-[#9aa7b4]">
              Your room stays at {money(trailing)} however well you trade — the floor follows your
              peak forever. On Apex that is the Tradovate behaviour;{" "}
              <a href="/rules" className="text-[#4da3ff] underline underline-offset-4">
                on Rithmic it stops
              </a>
              .
            </p>
          )}

          {ddType === "intraday" && (
            <p className="mt-2 text-[13px] text-[#9aa7b4]">
              Intraday trailing follows your peak{" "}
              <b className="text-[#e8edf3]">including unrealised profit</b> — a floating winner that round-trips still ratchets this floor up
              permanently. Bank profit; don&apos;t admire it.
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

          {/* The headline number: risk vs the real failure buffer, not the advertised size. */}
          {contracts >= 1 && cushion > 0 && (
            <div className="mt-4 rounded-lg border border-[#2a333f] bg-[#12161c] p-4">
              <div className="text-[12px] uppercase tracking-[0.1em] text-[#9aa7b4]">
                Risk as a share of your remaining failure buffer
              </div>
              <div className="mt-1 flex items-end gap-3">
                <div className="text-3xl font-extrabold" style={{ color: bufferTone }}>
                  {bufferPct.toFixed(1)}%
                </div>
                <div className="pb-1 text-[13px] text-[#9aa7b4]">
                  of the {money(cushion)} standing between you and a blown account
                </div>
              </div>
              <p className="mt-2 text-[13px] text-[#9aa7b4]">
                The same trade is only <b className="text-[#e8edf3]">{accountPct.toFixed(2)}%</b> of your{" "}
                {money(startingBalance)} headline account — which is why that number misleads. You have{" "}
                <b style={{ color: bufferTone }}>{isFinite(fullStopsLeft) ? fullStopsLeft : "∞"}</b> full stops
                left before breach.
              </p>
            </div>
          )}

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
