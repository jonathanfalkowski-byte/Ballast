"""
Parameter sweep: does ANY configuration of these setups show an edge?

The honest way to run a sweep is to report the whole distribution of results,
not the best one. With N configurations tested, some will look good by luck.
So this prints the observed t-statistic distribution next to what the null
hypothesis (no edge) predicts, and applies a Bonferroni threshold to the best.
"""
import sys, math, itertools, bisect
sys.path.insert(0, '.')
import backtest as bt

days = bt.load_seconds('mnq_seconds.csv')
DAYKEYS = sorted(days)

# --- precompute per (day, bar_seconds): bars, emas, pivots, sec-time index ---
CACHE = {}
def prep(d, bs):
    key = (d, bs)
    if key in CACHE: return CACHE[key]
    secs = days[d]
    bars = bt.build_bars(secs, bs)
    if len(bars) < 120:
        CACHE[key] = None; return None
    cl = [b["c"] for b in bars]
    obj = {"secs": secs, "bars": bars,
           "e30": bt.ema(cl, 30), "e100": bt.ema(cl, 100),
           "piv": bt.pivots(bars),
           "stimes": [s[1] for s in secs]}
    CACHE[key] = obj
    return obj

def resolve_bracket(secs, start_idx, direction, entry, stop_t, targ_t):
    if direction > 0:
        tgt, stp = entry + targ_t*bt.TICK, entry - stop_t*bt.TICK
    else:
        tgt, stp = entry - targ_t*bt.TICK, entry + stop_t*bt.TICK
    for j in range(start_idx, len(secs)):
        _, _, _, h, l, _, _, _, _ = secs[j]
        if direction > 0: hit_s, hit_t = l <= stp, h >= tgt
        else:             hit_s, hit_t = h >= stp, l <= tgt
        if hit_s: return -stop_t, j
        if hit_t: return targ_t, j
    return None, len(secs)-1

def net(ticks): return ticks*bt.MNQ_TICK_VALUE - bt.COMMISSION_RT - bt.SLIP_TICKS*bt.MNQ_TICK_VALUE

def run(which, bs, field, mod, strong, strength, stop_t, targ_t, fade):
    nets = []; wins = 0
    for d in DAYKEYS:
        p = prep(d, bs)
        if p is None: continue
        bars, secs = p["bars"], p["secs"]
        dot = bt.dots(bars, field, 20, mod, strong)
        if which == "A":
            sig = bt.signals_A(bars, dot, p["e30"], p["e100"], strength)
        else:
            sig = bt.signals_B(bars, dot, p["piv"], strength, 10)
        busy = -1
        for bi, direction in sig:
            b = bars[bi]
            if not (bt.ET_OPEN_MIN <= b["et_min"] < bt.ET_CLOSE_MIN): continue
            k = bisect.bisect_right(p["stimes"], b["sec_end"])
            if k >= len(secs) or k <= busy: continue
            if fade: direction = -direction
            res, xi = resolve_bracket(secs, k, direction, secs[k][2], stop_t, targ_t)
            if res is None: continue
            busy = xi
            nets.append(net(res))
            if res > 0: wins += 1
    n = len(nets)
    if n < 60: return None
    m = sum(nets)/n
    sd = math.sqrt(sum((x-m)**2 for x in nets)/(n-1))
    t = m/(sd/math.sqrt(n)) if sd else 0.0
    return {"n": n, "wr": 100*wins/n, "exp": m, "t": t}

GRID = list(itertools.product(
    ("A","B"),                       # setup
    (15, 30, 60),                    # bar seconds
    ("delta","fdelta"),              # delta series
    ((1.0,3.0),(0.5,2.0),(1.5,4.0)), # (moderate, strong) dot thresholds
    (1,2),                           # any dot / strong only
    ((135,141),(100,100),(50,50),(80,160),(160,80)),  # stop, target
    (False,True),                    # follow / fade
))
print(f"configurations: {len(GRID)}\n", flush=True)

rows = []
for i,(which,bs,field,(mod,st),strength,(stop_t,targ_t),fade) in enumerate(GRID):
    r = run(which,bs,field,mod,st,strength,stop_t,targ_t,fade)
    if r:
        r["label"] = (f"{which} {bs:>3}s {field:6} m/s={mod}/{st} "
                      f"{'strong' if strength==2 else 'any   '} "
                      f"{stop_t}/{targ_t} {'FADE' if fade else 'FOLL'}")
        rows.append(r)
    if (i+1) % 60 == 0: print(f"  ...{i+1}/{len(GRID)}", flush=True)

rows.sort(key=lambda r: -r["t"])
print(f"\ncompleted {len(rows)} configurations with n>=60\n")
print("TOP 10 BY t-STATISTIC")
for r in rows[:10]:
    print(f"  {r['label']}  n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  t={r['t']:+5.2f}")
print("\nBOTTOM 5")
for r in rows[-5:]:
    print(f"  {r['label']}  n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  t={r['t']:+5.2f}")

pos = sum(1 for r in rows if r["exp"] > 0)
ts  = [r["t"] for r in rows]
mean_t = sum(ts)/len(ts)
print(f"\nprofitable configurations: {pos}/{len(rows)} ({100*pos/len(rows):.1f}%)")
print(f"mean t across all configs: {mean_t:+.2f}   (null predicts ~0.00)")
print(f"max t observed: {max(ts):+.2f}")
# Bonferroni: two-sided 5% over N tests -> need |t| ~ that of alpha/N
import statistics
alpha_adj = 0.05/len(rows)
z_needed = statistics.NormalDist().inv_cdf(1 - alpha_adj/2)
print(f"Bonferroni threshold for {len(rows)} tests at 5%: |t| > {z_needed:.2f}")
print("VERDICT:", "an edge survives multiple testing" if max(ts) > z_needed
      else "NO configuration survives multiple-testing correction")
