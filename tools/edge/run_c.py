import sys, math, bisect, itertools, statistics, random
sys.path.insert(0,'.')
import backtest as bt
from bollinger import bollinger, signals_C1, signals_C2

days = bt.load_seconds('mnq_seconds.csv')
IS  = [d for d in sorted(days) if d <  "20260401"]   # develop here
OOS = [d for d in sorted(days) if d >= "20260401"]   # do not touch until locked
print(f"in-sample sessions={len(IS)}   out-of-sample sessions={len(OOS)}\n")

CACHE={}
def prep(d, bs, bb_p, bb_m):
    k=(d,bs,bb_p,bb_m)
    if k in CACHE: return CACHE[k]
    secs=days[d]; bars=bt.build_bars(secs,bs)
    if len(bars)<120: CACHE[k]=None; return None
    cl=[b["c"] for b in bars]
    mid,up,lo = bollinger(cl,bb_p,bb_m)
    CACHE[k]={"secs":secs,"bars":bars,"up":up,"lo":lo,
              "stimes":[s[1] for s in secs]}
    return CACHE[k]

def go(daylist, variant, bs, field, strength, bb_p, bb_m, stop_t, targ_t, max_wait=10):
    nets=[]; wins=0
    for d in daylist:
        p=prep(d,bs,bb_p,bb_m)
        if p is None: continue
        bars,secs=p["bars"],p["secs"]
        dot=bt.dots(bars,field,20,1.0,3.0)
        if variant=="C1": sig=signals_C1(bars,dot,p["up"],p["lo"],strength,max_wait)
        else:             sig=signals_C2(bars,dot,p["up"],p["lo"],strength)
        busy=-1
        for bi,direction in sig:
            b=bars[bi]
            if not (bt.ET_OPEN_MIN<=b["et_min"]<bt.ET_CLOSE_MIN): continue
            k=bisect.bisect_right(p["stimes"],b["sec_end"])
            if k>=len(secs) or k<=busy: continue
            if direction>0: tgt,stp=secs[k][2]+targ_t*bt.TICK, secs[k][2]-stop_t*bt.TICK
            else:           tgt,stp=secs[k][2]-targ_t*bt.TICK, secs[k][2]+stop_t*bt.TICK
            res=None
            for j in range(k,len(secs)):
                _,_,_,h,l,_,_,_,_=secs[j]
                if direction>0: hs,ht = l<=stp, h>=tgt
                else:           hs,ht = h>=stp, l<=tgt
                if hs: res,xi=-stop_t,j; break
                if ht: res,xi= targ_t,j; break
            if res is None: continue
            busy=xi
            nets.append(res*bt.MNQ_TICK_VALUE - bt.COMMISSION_RT - bt.SLIP_TICKS*bt.MNQ_TICK_VALUE)
            if res>0: wins+=1
    n=len(nets)
    if n<40: return None
    m=sum(nets)/n
    sd=math.sqrt(sum((x-m)**2 for x in nets)/(n-1)) if n>1 else 0
    t=m/(sd/math.sqrt(n)) if sd else 0
    return {"n":n,"wr":100*wins/n,"exp":m,"t":t,"total":sum(nets)}

GRID=list(itertools.product(
    ("C1","C2"), (30,60), ("delta","fdelta"), (1,2),
    ((20,2.0),(20,2.5),(14,2.0)), ((135,141),(100,100),(60,120))))
print(f"IN-SAMPLE search: {len(GRID)} configurations (Jan-Mar only)\n")
res=[]
for v,bs,field,st,(bp,bm),(s_t,t_t) in GRID:
    r=go(IS,v,bs,field,st,bp,bm,s_t,t_t)
    if r:
        r["cfg"]=(v,bs,field,st,bp,bm,s_t,t_t)
        r["label"]=f"{v} {bs}s {field:6} {'strong' if st==2 else 'any   '} BB{bp}/{bm} {s_t}/{t_t}"
        res.append(r)
res.sort(key=lambda r:-r["t"])
print("TOP 8 IN-SAMPLE")
for r in res[:8]:
    print(f"  {r['label']}  n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  t={r['t']:+5.2f}")
print("\nBOTTOM 3 IN-SAMPLE")
for r in res[-3:]:
    print(f"  {r['label']}  n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  t={r['t']:+5.2f}")
pos=sum(1 for r in res if r["exp"]>0)
print(f"\nprofitable in-sample: {pos}/{len(res)}   mean t={statistics.mean(r['t'] for r in res):+.2f}")

best=res[0]
print(f"\n{'='*74}\nLOCKED CONFIG (best in-sample): {best['label']}")
print(f"  in-sample : n={best['n']} WR={best['wr']:.1f}% exp=${best['exp']:+.2f} t={best['t']:+.2f}")
o=go(OOS,*best["cfg"])
if o:
    print(f"  OUT-OF-SAMPLE: n={o['n']} WR={o['wr']:.1f}% exp=${o['exp']:+.2f} t={o['t']:+.2f} total=${o['total']:+.0f}")
else:
    print("  OUT-OF-SAMPLE: too few trades")
print("="*74)
print("\nOOS check on the top 5, to see if ANY of them carries over:")
for r in res[:5]:
    o=go(OOS,*r["cfg"])
    tag = f"n={o['n']:<5} WR={o['wr']:5.1f}% exp=${o['exp']:+6.2f} t={o['t']:+5.2f}" if o else "too few trades"
    print(f"  {r['label']}  IS t={r['t']:+5.2f}  ->  OOS {tag}")
