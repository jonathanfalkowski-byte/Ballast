"use client";

import { useEffect, useMemo, useState } from "react";
import {
  cushionToFloor,
  floorLevel,
  floorIsLocked,
  peakThatLocks,
} from "@/lib/disciplineEngine";
import { RULES_TEXT } from "@/lib/propFirmRules";

// $/tick. All four instruments use a 0.25 tick size (4 ticks per point).
const TICK_VALUE: Record<string, number> = { ES: 12.5, NQ: 5, MES: 1.25, MNQ: 0.5 };
const tickValue = (inst: string) => TICK_VALUE[inst] ?? 0;

type Kind = "eval_rithmic" | "eval_tradovate" | "funded" | "own";

type Setup = {
  id: number;
  name: string;
  inst: string;
  stopTicks: number;
  targetTicks: number;
  contracts: number;
};

type BookAccount = {
  firm: string;
  plan: string;
  size: number;
  drawdown: number;
  ddType: "intraday" | "end_of_day";
  target: number;
  lockAt: number;
};

function money(n: number) {
  return "$" + Math.round(n).toLocaleString();
}

function sizeLabel(size: number) {
  return size >= 1000 ? size / 1000 + "K" : String(size);
}

// Parse the shared rule book (same pipe format the add-on reads) into pickable accounts.
function parseBook(text: string): BookAccount[] {
  const out: BookAccount[] = [];
  for (const raw of text.split("\n")) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    const f = line.split("|");
    const head = (f[0] ?? "").trim().toUpperCase();
    if (head === "VERSION" || head === "VERIFIED") continue;
    if (f.length < 7) continue;
    const size = parseFloat(f[2]);
    const drawdown = parseFloat(f[3]);
    if (!(size > 0) || !(drawdown > 0)) continue;
    const ddType: "intraday" | "end_of_day" =
      (f[4] ?? "").trim().toUpperCase() === "INTRADAY" ? "intraday" : "end_of_day";
    const target = parseFloat(f[6]) || 0;
    const lockAt = f.length >= 9 ? parseFloat(f[8]) || 0 : 0;
    out.push({ firm: f[0].trim(), plan: f[1].trim(), size, drawdown, ddType, target, lockAt });
  }
  return out;
}

const BOOK = parseBook(RULES_TEXT);
const BOOK_FIRMS: string[] = [];
const BOOK_BY_FIRM: Record<string, { a: BookAccount; i: number }[]> = {};
BOOK.forEach((a, i) => {
  if (!BOOK_BY_FIRM[a.firm]) {
    BOOK_BY_FIRM[a.firm] = [];
    BOOK_FIRMS.push(a.firm);
  }
  BOOK_BY_FIRM[a.firm].push({ a, i });
});

function kindFromAccount(a: BookAccount): Kind {
  const p = a.plan.toLowerCase();
  if (p.includes("tradovate") || a.lockAt <= 0) return "eval_tradovate";
  if (p.includes("funded") || p.includes("pa") || p.includes("live")) return "funded";
  if (a.firm.toLowerCase().includes("own")) return "own";
  return "eval_rithmic";
}

export default function CushionCalculator() {
  const [startingBalance, setStart] = useState(150000);
  const [trailing, setTrailing] = useState(5000);
  const [current, setCurrent] = useState(151200);
  const [peak, setPeak] = useState(152000);
  const [ddType, setDdType] = useState<"intraday" | "end_of_day">("intraday");
  const [kind, setKind] = useState<Kind>("eval_rithmic");
  const [target, setTarget] = useState(9000);

  // When a book account is picked, its exact lock level (from the rule book) overrides
  // the kind-derived one. null = manual entry (derive the lock from the kind dropdown).
  const [lockOverride, setLockOverride] = useState<number | null>(null);
  const [picked, setPicked] = useState("");

  function pickAccount(idx: number) {
    const a = BOOK[idx];
    if (!a) {
      setPicked("");
      setLockOverride(null);
      return;
    }
    setStart(a.size);
    setTrailing(a.drawdown);
    setCurrent(a.size);
    setPeak(a.size);
    setDdType(a.ddType);
    setTarget(a.target);
    setKind(kindFromAccount(a));
    setLockOverride(a.lockAt);
    setPicked(String(idx));
  }

  function manualKind(k: Kind) {
    setKind(k);
    setLockOverride(null);
    setPicked("");
  }

  const derivedLock = useMemo(() => {
    if (kind === "eval_tradovate") return 0;
    if (kind === "funded") return startingBalance + 100;
    if (kind === "eval_rithmic") return startingBalance + target;
    if (kind === "own") return startingBalance - trailing;
    return 0;
  }, [kind, startingBalance, target, trailing]);

  const lockFloorAt = lockOverride !== null ? lockOverride : derivedLock;

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
  const toGo = locksAtPeak > 0 ? Math.max(0, locksAtPeak - Math.max(peak, current)) : 0;

  return (
    <div className="space-y-6">
      {/* Account + cushion */}
      <Card title="Your account">
        <div className="flex flex-col gap-1">
          <label className="text-[13px] text-[#9aa7b4]">Pick your account (from the rule book)</label>
          <select
            value={picked}
            onChange={(e) => pickAccount(parseInt(e.target.value, 10))}
            className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
          >
            <option value="">— Enter manually —</option>
            {BOOK_FIRMS.map((firm) => (
              <optgroup key={firm} label={firm}>
                {BOOK_BY_FIRM[firm].map(({ a, i }) => (
                  <option key={i} value={i}>
                    {a.plan} · {sizeLabel(a.size)}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
          <span className="text-[12px] text-[#7f8b98]">
            Picking an account fills in the rules below &mdash; then set Current and Peak to your live
            balances.
          </span>
        </div>

        <div className="mt-4">
          <Grid>
            <NumField label="Starting balance ($)" value={startingBalance} onChange={setStart} />
            <NumField label="Trailing drawdown ($)" value={trailing} onChange={setTrailing} />
            <NumField
              label="Current balance ($)"
              value={current}
              onChange={setCurrent}
              help="Your account balance right now."
            />
            <NumField
              label="Peak balance ($)"
              value={peak}
              onChange={setPeak}
              help="The highest your balance has ever reached — the high-water mark the trailing floor follows. If you're unsure, set it equal to your current balance."
            />
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
              <label className="text-[13px] text-[#9aa7b4]">Lock behaviour</label>
              <select
                value={kind}
                onChange={(e) => manualKind(e.target.value as Kind)}
                className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-3 py-2.5 text-[15px] outline-none focus:border-[#4da3ff]"
              >
                <option value="eval_rithmic">Eval &mdash; Rithmic / WealthCharts</option>
                <option value="eval_tradovate">Eval &mdash; Tradovate</option>
                <option value="funded">Funded / PA</option>
                <option value="own">My own money (fixed max loss)</option>
              </select>
            </div>
            <NumField label="Profit target to pass ($)" value={target} onChange={setTarget} />
          </Grid>
        </div>

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

          {lockFloorAt > 0 && !locked && (
            <p className="mt-4 border-t border-[#2a333f] pt-4 text-[13px] text-[#9aa7b4]">
              Until then, <b className="text-[#e8edf3]">making money buys you no extra room</b> &mdash; the
              floor climbs with you and your cushion stays at {money(trailing)}. You need another{" "}
              <b className="text-[#e8edf3]">{money(toGo)}</b> of peak before that changes.
            </p>
          )}
          {lockFloorAt > 0 && locked && (
            <p className="mt-4 border-t border-[#2a333f] pt-4 text-[13px] text-[#9aa7b4]">
              Your floor has stopped moving. From here every dollar you make is genuine cushion, which
              is why this is the point the whole evaluation turns on.
            </p>
          )}
          {lockFloorAt <= 0 && (
            <p className="mt-4 border-t border-[#2a333f] pt-4 text-[13px] text-[#9aa7b4]">
              Your room stays at {money(trailing)} however well you trade &mdash; the floor follows your
              peak forever. On Apex that is the Tradovate behaviour;{" "}
              <a href="/rules" className="text-[#4da3ff] underline underline-offset-4">
                on Rithmic it stops
              </a>
              .
            </p>
          )}
          {ddType === "intraday" && (
            <p className="mt-2 text-[13px] text-[#9aa7b4]">
              Intraday trailing follows your peak <b className="text-[#e8edf3]">including unrealised
              profit</b> — a floating winner that round-trips still ratchets this floor up permanently.
              Bank profit; don&apos;t admire it.
            </p>
          )}
        </Out>
      </Card>

      <SetupsCard cushion={cushion} />
    </div>
  );
}

function SetupsCard({ cushion }: { cushion: number }) {
  const [setups, setSetups] = useState<Setup[]>([
    { id: 1, name: "Renko 50", inst: "ES", stopTicks: 135, targetTicks: 141, contracts: 2 },
    { id: 2, name: "Renko 80", inst: "ES", stopTicks: 250, targetTicks: 350, contracts: 2 },
  ]);
  const [nextId, setNextId] = useState(3);

  const update = (id: number, patch: Partial<Setup>) =>
    setSetups((list) => list.map((x) => (x.id === id ? { ...x, ...patch } : x)));
  const remove = (id: number) => setSetups((list) => list.filter((x) => x.id !== id));
  const add = () => {
    setSetups((list) => [
      ...list,
      { id: nextId, name: "New setup", inst: "ES", stopTicks: 40, targetTicks: 80, contracts: 1 },
    ]);
    setNextId((n) => n + 1);
  };

  return (
    <Card title="Your setups - risk per trade vs your cushion">
      <p className="text-[13px] text-[#9aa7b4]">
        Enter each setup the way you actually trade it (stops in ticks, and set the contracts). Ballast
        shows what a full stop costs and how much of your remaining failure buffer &mdash; the number
        above &mdash; it eats. That is what decides whether a strategy you have an edge on can still
        quietly end the account.
      </p>
      <div className="mt-4 space-y-3">
        {setups.map((s) => (
          <SetupRow
            key={s.id}
            s={s}
            cushion={cushion}
            onChange={(p) => update(s.id, p)}
            onRemove={() => remove(s.id)}
          />
        ))}
        {setups.length === 0 && (
          <p className="text-[13px] text-[#7f8b98]">No setups yet &mdash; add one below.</p>
        )}
      </div>
      <button
        onClick={add}
        className="mt-3 rounded-lg border border-[#2a333f] px-4 py-2 text-[13px] text-[#9aa7b4] hover:border-[#4da3ff] hover:text-[#4da3ff]"
      >
        + Add a setup
      </button>
      <p className="mt-4 text-[12px] text-[#7f8b98]">
        Descriptive risk math from your own numbers and the cushion above &mdash; for risk management
        only. Not financial advice, and not a prediction of results.
      </p>
    </Card>
  );
}

function SetupRow({
  s,
  cushion,
  onChange,
  onRemove,
}: {
  s: Setup;
  cushion: number;
  onChange: (p: Partial<Setup>) => void;
  onRemove: () => void;
}) {
  const tv = tickValue(s.inst);
  const risk = s.stopTicks * tv * s.contracts;
  const reward = s.targetTicks * tv * s.contracts;
  const rr = s.stopTicks > 0 ? s.targetTicks / s.stopTicks : 0;
  const bufferPct = cushion > 0 ? (risk / cushion) * 100 : Infinity;
  const stopsLeft = risk > 0 ? Math.floor(cushion / risk) : Infinity;
  const breaches = cushion > 0 && risk >= cushion;
  const tone = bufferPct >= 33 ? "#f4523b" : bufferPct >= 15 ? "#e3b341" : "#3fb950";

  return (
    <div className="rounded-lg border border-[#2a333f] bg-[#12161c] p-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1">
          <span className="text-[12px] text-[#9aa7b4]">Setup</span>
          <input
            value={s.name}
            onChange={(e) => onChange({ name: e.target.value })}
            className="w-32 rounded-lg border border-[#2a333f] bg-[#0e141b] px-2 py-1.5 text-[14px] outline-none focus:border-[#4da3ff]"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-[12px] text-[#9aa7b4]">Instrument</span>
          <select
            value={s.inst}
            onChange={(e) => onChange({ inst: e.target.value })}
            className="rounded-lg border border-[#2a333f] bg-[#0e141b] px-2 py-1.5 text-[14px] outline-none focus:border-[#4da3ff]"
          >
            <option value="ES">ES</option>
            <option value="NQ">NQ</option>
            <option value="MES">MES</option>
            <option value="MNQ">MNQ</option>
          </select>
        </label>
        <NumField compact label="Stop (ticks)" value={s.stopTicks} onChange={(n) => onChange({ stopTicks: n })} />
        <NumField compact label="Target (ticks)" value={s.targetTicks} onChange={(n) => onChange({ targetTicks: n })} />
        <NumField compact label="Contracts" value={s.contracts} onChange={(n) => onChange({ contracts: n })} />
        <button onClick={onRemove} className="ml-auto text-[13px] text-[#7f8b98] hover:text-[#f4523b]">
          Remove
        </button>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-x-6 gap-y-1 text-[13px]">
        <span className="text-[#9aa7b4]">
          Risk per stop: <b className="text-[#e8edf3]">{money(risk)}</b>
        </span>
        <span className="text-[#9aa7b4]">
          Reward: <b className="text-[#e8edf3]">{money(reward)}</b> ({rr.toFixed(2)}R)
        </span>
        <span className="text-[#9aa7b4]">
          Share of buffer:{" "}
          <b style={{ color: tone }}>{isFinite(bufferPct) ? bufferPct.toFixed(1) + "%" : "—"}</b>
        </span>
        <span className="text-[#9aa7b4]">
          Full stops left: <b style={{ color: tone }}>{isFinite(stopsLeft) ? stopsLeft : "∞"}</b>
        </span>
      </div>
      {breaches && (
        <p className="mt-2 text-[13px] text-[#f4523b]">
          A single full stop on this setup breaches your floor &mdash; this trade alone can end the
          account.
        </p>
      )}
      {!breaches && bufferPct >= 33 && (
        <p className="mt-2 text-[13px] text-[#e3b341]">
          One stop here is {bufferPct.toFixed(0)}% of everything standing between you and a blown
          account.
        </p>
      )}
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

// Numeric field backed by a local string so leading zeros delete normally (a plain
// number input fights you), while still syncing when the value is set from outside
// (e.g. the account picker).
function NumField({
  label,
  value,
  onChange,
  help,
  compact,
}: {
  label: string;
  value: number;
  onChange: (n: number) => void;
  help?: string;
  compact?: boolean;
}) {
  const [raw, setRaw] = useState(String(value));

  useEffect(() => {
    // The account picker is an external source for this local editing buffer.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (parseFloat(raw) !== value) setRaw(value === 0 ? "" : String(value));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  return (
    <div className="flex flex-col gap-1">
      <label className={compact ? "text-[12px] text-[#9aa7b4]" : "text-[13px] text-[#9aa7b4]"}>
        {label}
      </label>
      <input
        inputMode="decimal"
        value={raw}
        onChange={(e) => {
          const v = e.target.value;
          setRaw(v);
          const n = parseFloat(v);
          onChange(Number.isFinite(n) ? n : 0);
        }}
        className={
          (compact ? "w-24 px-2 py-1.5 text-[14px] " : "px-3 py-2.5 text-[15px] ") +
          "rounded-lg border border-[#2a333f] bg-[#0e141b] outline-none focus:border-[#4da3ff]"
        }
      />
      {help && !compact && <span className="text-[12px] text-[#7f8b98]">{help}</span>}
    </div>
  );
}
