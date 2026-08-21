"""
Rerun the whole study on REAL NQ order flow.

Signals and price action come from NQ (the instrument the indicator actually
runs on). Profit and loss is scored in MNQ terms ($0.50/tick, $1.24 round turn)
because that is what the trader actually trades. Prices track within a tick and
the bracket is defined in ticks, so this is the faithful simulation.

Three-way split, and the last block is treated as sacred:
    TRAIN    Jan 1  - Mar 16   choose parameters here
    VALIDATE Apr 1  - Jun 12   previously used, so no longer clean
    HOLDOUT  Jul 1  - Aug 19   NEVER touched by any prior test in this project
"""
import sys, math, bisect, itertools, statistics, random
sys.path.insert(0, '.')
import backtest as bt
from bollinger import bollinger, signals_C1, signals_C2

PATH = sys.argv[1] if len(sys.argv) > 1 else 'nq_seconds.csv'
days = bt.load_seconds(PATH)
TRAIN   = [d for d in sorted(days) if d <  "20260401"]
VALID   = [d for d in sorted(days) if "20260401" <= d < "20260701"]
HOLDOUT = [d for d in sorted(days) if d >= "20260701"]
print(f"TRAIN {len(TRAIN)}  VALIDATE {len(VALID)}  HOLDOUT {len(HOLDOUT)} sessions")
print(f"breakeven WR after costs = {100*bt.breakeven_wr():.1f}%\n")

def stats(nets, wins):
    n = len(nets)
    if n < 40: return None
    m = sum(nets)/n
    sd = math.sqrt(sum((x-m)**2 for x in nets)/(n-1)) if n > 1 else 0
    return {"n": n, "wr": 100*wins/n, "exp": m,
            "t": m/(sd/math.sqrt(n)) if sd else 0.0, "total": sum(nets)}

def core(daylist, which, bs, field, strength, stop_t=135, targ_t=141):
    nets=[]; wins=0
    for d in daylist:
        secs = days[d]; bars = bt.build_bars(secs, bs)
        if len(bars) < 120: continue
        cl=[b["c"] for b in bars]
        dot = bt.dots(bars, field, 20, 1.0, 3.0)
        if which=="A": sig = bt.signals_A(bars, dot, bt.ema(cl,30), bt.ema(cl,100), strength)
        else:          sig = bt.signals_B(bars, dot, bt.pivots(bars), strength, 10)
        st=[s[1] for s in secs]; busy=-1
        for bi,direction in sig:
            b=bars[bi]
            if not (bt.ET_OPEN_MIN<=b["et_min"]<bt.ET_CLOSE_MIN): continue
            k=bisect.bisect_right(st,b["sec_end"])
            if k>=len(secs) or k<=busy: continue
            res,xi = bt.resolve(secs,k,direction,secs[k][2])
            if res is None: continue
            busy=xi; nets.append(bt.net_dollars(res))
            if res>0: wins+=1
    return stats(nets,wins)

def rand_control(daylist, seed, n_per_day=25):
    rnd=random.Random(seed); nets=[]; wins=0
    for d in daylist:
        secs=days[d]
        idx=[k for k,s in enumerate(secs) if bt.ET_OPEN_MIN<=s[0]<bt.ET_CLOSE_MIN]
        if len(idx)<500: continue
        busy=-1
        for k in sorted(rnd.sample(idx,min(n_per_day,len(idx)))):
            if k<=busy: continue
            direction=rnd.choice((1,-1))
            res,xi=bt.resolve(secs,k,direction,secs[k][2])
            if res is None: continue
            busy=xi; nets.append(bt.net_dollars(res))
            if res>0: wins+=1
    return stats(nets,wins)

def line(lab, r):
    if not r: return f"  {lab:<34} too few trades"
    return (f"  {lab:<34} n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  "
            f"t={r['t']:+5.2f}  total=${r['total']:+8.0f}")

ALL = TRAIN + VALID + HOLDOUT
print("="*92)
print("SETUPS A AND B ON REAL NQ FLOW  (all sessions, uncapped, 135/141)")
print("="*92)
for field in ("delta","fdelta"):
    for which in ("A","B"):
        for st,slab in ((2,"strong"),(1,"any   ")):
            print(line(f"{which} {field:6} {slab}", core(ALL,which,30,field,st)))
print()
exps=[]; wrs=[]
for s in range(1,21):
    r=rand_control(ALL,s)
    if r: exps.append(r["exp"]); wrs.append(r["wr"])
m,sd = statistics.mean(exps), statistics.stdev(exps)
print(f"  RANDOM CONTROL (20 seeds)          mean exp=${m:+.2f}  sd=${sd:.2f}  "
      f"WR={statistics.mean(wrs):.2f}%   95% band ${m-1.96*sd:+.2f} to ${m+1.96*sd:+.2f}")
print(f"\n  -> setups outside that band are the only ones worth a second look.")
