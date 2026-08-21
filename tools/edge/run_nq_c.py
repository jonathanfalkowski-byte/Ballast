"""
The Bollinger-filtered dot idea, on real NQ flow, with a sealed holdout.

Protocol, fixed before looking at anything:
  1. Search 144 configurations on TRAIN (Jan-Mar) only.
  2. Carry the top 5 to VALIDATE (Apr-Jun).
  3. Lock ONE configuration - the best on TRAIN - and test it once on
     HOLDOUT (Jul-Aug), which no test in this project has ever touched.
A configuration that is positive in all three is the only outcome that counts.
"""
import sys, math, bisect, itertools, statistics
sys.path.insert(0,'.')
import backtest as bt
from bollinger import bollinger, signals_C1, signals_C2

days = bt.load_seconds(sys.argv[1] if len(sys.argv)>1 else 'nq_seconds.csv')
TRAIN   = [d for d in sorted(days) if d <  "20260401"]
VALID   = [d for d in sorted(days) if "20260401" <= d < "20260701"]
HOLDOUT = [d for d in sorted(days) if d >= "20260701"]
print(f"TRAIN {len(TRAIN)}   VALIDATE {len(VALID)}   HOLDOUT {len(HOLDOUT)} sessions\n")

CACHE={}
def prep(d,bs,bp,bm):
    k=(d,bs,bp,bm)
    if k in CACHE: return CACHE[k]
    secs=days[d]; bars=bt.build_bars(secs,bs)
    if len(bars)<120: CACHE[k]=None; return None
    cl=[b["c"] for b in bars]
    _,up,lo = bollinger(cl,bp,bm)
    CACHE[k]={"secs":secs,"bars":bars,"up":up,"lo":lo,"st":[s[1] for s in secs]}
    return CACHE[k]

def go(daylist, variant, bs, field, strength, bp, bm, stop_t, targ_t, max_wait=10):
    nets=[]; wins=0
    for d in daylist:
        p=prep(d,bs,bp,bm)
        if p is None: continue
        bars,secs=p["bars"],p["secs"]
        dot=bt.dots(bars,field,20,1.0,3.0)
        sig = (signals_C1(bars,dot,p["up"],p["lo"],strength,max_wait) if variant=="C1"
               else signals_C2(bars,dot,p["up"],p["lo"],strength))
        busy=-1
        for bi,direction in sig:
            b=bars[bi]
            if not (bt.ET_OPEN_MIN<=b["et_min"]<bt.ET_CLOSE_MIN): continue
            k=bisect.bisect_right(p["st"],b["sec_end"])
            if k>=len(secs) or k<=busy: continue
            e=secs[k][2]
            if direction>0: tgt,stp=e+targ_t*bt.TICK, e-stop_t*bt.TICK
            else:           tgt,stp=e-targ_t*bt.TICK, e+stop_t*bt.TICK
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
    return {"n":n,"wr":100*wins/n,"exp":m,"t":m/(sd/math.sqrt(n)) if sd else 0,"total":sum(nets)}

GRID=list(itertools.product(("C1","C2"),(30,60),("delta","fdelta"),(1,2),
                            ((20,2.0),(20,2.5),(14,2.0)),((135,141),(100,100),(60,120))))
res=[]
for v,bs,field,st,(bp,bm),(s_t,t_t) in GRID:
    r=go(TRAIN,v,bs,field,st,bp,bm,s_t,t_t)
    if r:
        r["cfg"]=(v,bs,field,st,bp,bm,s_t,t_t)
        r["label"]=f"{v} {bs}s {field:6} {'strong' if st==2 else 'any   '} BB{bp}/{bm} {s_t}/{t_t}"
        res.append(r)
res.sort(key=lambda r:-r["t"])
print(f"STEP 1 - search on TRAIN: {len(res)} configs, "
      f"{sum(1 for r in res if r['exp']>0)} profitable, mean t={statistics.mean(r['t'] for r in res):+.2f}\n")
print("STEP 2 - top 5 from TRAIN, carried to VALIDATE")
for r in res[:5]:
    v=go(VALID,*r["cfg"])
    vs = f"n={v['n']:<5} exp=${v['exp']:+6.2f} t={v['t']:+5.2f}" if v else "too few"
    print(f"  {r['label']}   TRAIN exp=${r['exp']:+6.2f} t={r['t']:+5.2f}  ->  VALID {vs}")

best=res[0]
print(f"\nSTEP 3 - LOCKED: {best['label']}")
for nm,dl in (("TRAIN",TRAIN),("VALIDATE",VALID),("HOLDOUT (never seen)",HOLDOUT)):
    r=go(dl,*best["cfg"])
    print(f"  {nm:<22} " + (f"n={r['n']:<5} WR={r['wr']:5.1f}%  exp=${r['exp']:+6.2f}  t={r['t']:+5.2f}  total=${r['total']:+8.0f}"
                            if r else "too few trades"))
print("\nAlso: every top-5 config on the sealed HOLDOUT, for completeness")
for r in res[:5]:
    h=go(HOLDOUT,*r["cfg"])
    hs = f"n={h['n']:<5} WR={h['wr']:5.1f}% exp=${h['exp']:+6.2f} t={h['t']:+5.2f}" if h else "too few"
    print(f"  {r['label']}  ->  HOLDOUT {hs}")
