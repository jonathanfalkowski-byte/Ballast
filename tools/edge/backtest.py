"""
Backtest the two precommitted setups against reconstructed ninZa-style vol-delta dots.

Setup A: bar crosses the 30 or 100 EMA AND prints a dot in the same direction.
Setup B: a confirmed 3-bar swing pivot, then the FIRST dot after it.

Exits are fixed: 135-tick stop / 141-tick target, resolved second-by-second so
stop-vs-target ordering inside a bar is decided by real data, not a guess.
Ties inside a single second resolve as a LOSS (pessimistic).

DOT MODEL (reconstruction, not the vendor's code):
    delta_i  = size-filtered bid/ask delta for the bar
    base_i   = SMA(|delta|, DELTA_PERIOD) over the preceding bars
    ratio    = |delta_i| / base_i
    ratio >= STRONG -> strong dot;  ratio >= MODERATE -> moderate dot
Params mirror the indicator's panel: Period Delta 20, Ratio Strong-Moderate 3.
"""
import csv, sys, math
from collections import defaultdict

TICK = 0.25
STOP_TICKS, TARGET_TICKS = 135, 141
MNQ_TICK_VALUE = 0.50          # $ per tick, Micro Nasdaq
COMMISSION_RT = 1.24           # $ round turn
SLIP_TICKS = 2                 # total round-turn slippage assumption

ET_OPEN_MIN, ET_CLOSE_MIN = 9*60+30, 12*60   # entry window, ET
DST_START, DST_END = "20260308", "20261101"

def et_minutes(datestr, hhmmss):
    off = 4 if DST_START <= datestr < DST_END else 5
    m = int(hhmmss[0:2])*60 + int(hhmmss[2:4])
    return m - off*60

def load_seconds(path):
    """date -> list of (et_min, sec_str, o,h,l,c, vol, delta, fdelta)"""
    days = defaultdict(list)
    with open(path) as fh:
        for r in csv.DictReader(fh):
            d = r["date"]; s = r["sec_utc"]
            days[d].append((et_minutes(d, s), s,
                            float(r["open"]), float(r["high"]),
                            float(r["low"]),  float(r["close"]),
                            int(r["vol"]), int(r["delta"]), int(r["fdelta"])))
    for d in days:
        days[d].sort(key=lambda x: x[1])
    return days

def build_bars(secs, bar_seconds):
    """Aggregate 1-second rows into fixed time bars. Returns list of dicts."""
    bars, cur = [], None
    for et_m, s, o, h, l, c, v, dl, fdl in secs:
        slot = (int(s[0:2])*3600 + int(s[2:4])*60 + int(s[4:6])) // bar_seconds
        if cur is None or cur["slot"] != slot:
            if cur: bars.append(cur)
            cur = {"slot": slot, "et_min": et_m, "sec": s, "o": o, "h": h,
                   "l": l, "c": c, "v": v, "delta": dl, "fdelta": fdl,
                   "sec_end": s}
        else:
            cur["h"] = max(cur["h"], h); cur["l"] = min(cur["l"], l)
            cur["c"] = c; cur["v"] += v; cur["sec_end"] = s
            cur["delta"] += dl; cur["fdelta"] += fdl
    if cur: bars.append(cur)
    return bars

def ema(vals, period):
    k = 2.0/(period+1); out = []; e = None
    for v in vals:
        e = v if e is None else v*k + e*(1-k)
        out.append(e)
    return out

def dots(bars, field, delta_period, moderate, strong):
    """-> list of 0 / +-1 (moderate) / +-2 (strong)"""
    out = [0]*len(bars)
    hist = []
    for i, b in enumerate(bars):
        d = b[field]
        if len(hist) >= delta_period:
            base = sum(abs(x) for x in hist[-delta_period:]) / delta_period
            if base > 0:
                ratio = abs(d)/base
                sign = 1 if d > 0 else (-1 if d < 0 else 0)
                if ratio >= strong:     out[i] = 2*sign
                elif ratio >= moderate: out[i] = 1*sign
        hist.append(d)
    return out

def pivots(bars):
    """Confirmed 3-bar swing: middle bar is the extreme. Known only at i+1.
       -> dict bar_index_of_confirmation -> +1 (swing low) / -1 (swing high)"""
    out = {}
    for i in range(1, len(bars)-1):
        a, b, c = bars[i-1], bars[i], bars[i+1]
        if b["l"] < a["l"] and b["l"] < c["l"]: out[i+1] = 1
        if b["h"] > a["h"] and b["h"] > c["h"]: out[i+1] = -1
    return out

def resolve(secs, start_idx, direction, entry):
    """Walk seconds forward; return (+TARGET_TICKS | -STOP_TICKS, exit_index)."""
    if direction > 0:
        tgt, stp = entry + TARGET_TICKS*TICK, entry - STOP_TICKS*TICK
    else:
        tgt, stp = entry - TARGET_TICKS*TICK, entry + STOP_TICKS*TICK
    for j in range(start_idx, len(secs)):
        _, _, _, h, l, _, _, _, _ = secs[j]
        if direction > 0:
            hit_s, hit_t = l <= stp, h >= tgt
        else:
            hit_s, hit_t = h >= stp, l <= tgt
        if hit_s: return -STOP_TICKS, j        # tie -> loss (pessimistic)
        if hit_t: return TARGET_TICKS, j
    return None, len(secs)-1                   # ran out of session

def signals_A(bars, dot, ema30, ema100, min_strength):
    """EMA cross + same-direction dot on the same bar."""
    sig = []
    for i in range(1, len(bars)):
        d = dot[i]
        if d == 0 or abs(d) < min_strength: continue
        prev, cur = bars[i-1], bars[i]
        up = ((prev["c"] <= ema30[i-1] and cur["c"] > ema30[i]) or
              (prev["c"] <= ema100[i-1] and cur["c"] > ema100[i]))
        dn = ((prev["c"] >= ema30[i-1] and cur["c"] < ema30[i]) or
              (prev["c"] >= ema100[i-1] and cur["c"] < ema100[i]))
        if up and d > 0: sig.append((i, 1))
        elif dn and d < 0: sig.append((i, -1))
    return sig

def signals_B(bars, dot, piv, min_strength, max_wait):
    """Confirmed pivot, then the FIRST direction-matching dot within max_wait bars."""
    sig = []; pending = None
    for i in range(len(bars)):
        if i in piv: pending = (piv[i], i)          # (direction, confirmed_at)
        if pending:
            want, at = pending
            if i - at > max_wait: pending = None; continue
            d = dot[i]
            if d != 0 and abs(d) >= min_strength and (d > 0) == (want > 0):
                sig.append((i, want)); pending = None
    return sig

def net_dollars(ticks):
    return ticks*MNQ_TICK_VALUE - COMMISSION_RT - SLIP_TICKS*MNQ_TICK_VALUE

def breakeven_wr():
    w, l = net_dollars(TARGET_TICKS), -net_dollars(-STOP_TICKS)
    return l/(w+l)

def run_setup(days, which, bar_seconds, field, delta_period, moderate, strong,
              min_strength=1, max_wait=10, daily_loss_cap=2):
    trades = []
    for d in sorted(days):
        secs = days[d]
        if len(secs) < 200: continue
        bars = build_bars(secs, bar_seconds)
        if len(bars) < 120: continue
        closes = [b["c"] for b in bars]
        e30, e100 = ema(closes, 30), ema(closes, 100)
        dot = dots(bars, field, delta_period, moderate, strong)
        if which == "A":
            sig = signals_A(bars, dot, e30, e100, min_strength)
        else:
            sig = signals_B(bars, dot, pivots(bars), min_strength, max_wait)

        losses = 0; busy_until = -1
        for bi, direction in sig:
            b = bars[bi]
            if not (ET_OPEN_MIN <= b["et_min"] < ET_CLOSE_MIN): continue
            if losses >= daily_loss_cap: break
            # enter at the open of the NEXT second after the signal bar closes
            si = next((k for k in range(len(secs)) if secs[k][1] > b["sec_end"]), None)
            if si is None or si <= busy_until: continue
            entry = secs[si][2]
            res, xi = resolve(secs, si, direction, entry)
            if res is None: continue
            busy_until = xi
            trades.append({"date": d, "dir": direction, "ticks": res,
                           "net": net_dollars(res)})
            if res < 0: losses += 1
    return trades

def summarize(trades, label):
    n = len(trades)
    if n == 0: return f"{label:<34} no trades"
    wins = sum(1 for t in trades if t["ticks"] > 0)
    nets = [t["net"] for t in trades]
    mean = sum(nets)/n
    var = sum((x-mean)**2 for x in nets)/(n-1) if n > 1 else 0.0
    sd = math.sqrt(var)
    t = mean/(sd/math.sqrt(n)) if sd > 0 else 0.0
    days = len(set(x["date"] for x in trades))
    return (f"{label:<34} n={n:<5} days={days:<4} WR={100*wins/n:5.1f}%  "
            f"exp=${mean:+7.2f}/trade  total=${sum(nets):+9.0f}  t={t:+5.2f}")

if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "mnq_seconds.csv"
    days = load_seconds(path)
    print(f"loaded {len(days)} sessions   breakeven WR = {100*breakeven_wr():.1f}%\n")
    for field in ("delta", "fdelta"):
        for bar_seconds in (30, 60):
            for which in ("A", "B"):
                tr = run_setup(days, which, bar_seconds, field, 20, 1.0, 3.0)
                print(summarize(tr, f"{which} {bar_seconds}s {field}"))
        print()
