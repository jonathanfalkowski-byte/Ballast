"""
Setup C: vol-delta dots filtered by Bollinger band context.

Two readings of the idea, both tested:

  C1  "touch and come back"  (the trader's own description)
      price tags the upper band, then closes back INSIDE it, and the first
      bearish dot after that is a short. Mirrored for the lower band.

  C2  "exhaustion fade"      (higher prior, given the forward-move result)
      a BULLISH dot printing while price is at/through the UPPER band is
      late buying into resistance -> short it immediately. Mirrored.

C2 exists because the forward-move study showed these dots lead price the
wrong way, which is what exhaustion looks like. C1 waits for confirmation and
gives up some of the move; C2 takes the exhaustion directly.

Protocol: parameters are chosen on Jan-Mar only. Apr-Jun is never looked at
until a single configuration has been locked.
"""
import math

def bollinger(closes, period=20, mult=2.0):
    mid, up, lo = [], [], []
    run = 0.0
    for i in range(len(closes)):
        if i + 1 < period:
            mid.append(None); up.append(None); lo.append(None); continue
        w = closes[i+1-period:i+1]
        m = sum(w)/period
        sd = math.sqrt(sum((x-m)**2 for x in w)/period)
        mid.append(m); up.append(m + mult*sd); lo.append(m - mult*sd)
    return mid, up, lo

def signals_C1(bars, dot, up, lo, min_strength, max_wait):
    """Tag the band, close back inside, then first confirming dot."""
    sig = []
    armed = None            # (direction_wanted, bar_index_of_reentry)
    touched_up = touched_dn = False
    for i, b in enumerate(bars):
        if up[i] is None: continue
        if b["h"] >= up[i]: touched_up = True; touched_dn = False
        if b["l"] <= lo[i]: touched_dn = True; touched_up = False

        # re-entry: was outside, now closed back inside
        if touched_up and b["c"] < up[i]:
            armed = (-1, i); touched_up = False
        elif touched_dn and b["c"] > lo[i]:
            armed = (1, i); touched_dn = False

        if armed:
            want, at = armed
            if i - at > max_wait: armed = None; continue
            d = dot[i]
            if d != 0 and abs(d) >= min_strength and (d > 0) == (want > 0):
                sig.append((i, want)); armed = None
    return sig

def signals_C2(bars, dot, up, lo, min_strength):
    """Bullish dot at the upper band -> short. Bearish dot at the lower -> long."""
    sig = []
    for i, b in enumerate(bars):
        if up[i] is None: continue
        d = dot[i]
        if d == 0 or abs(d) < min_strength: continue
        if d > 0 and b["h"] >= up[i]: sig.append((i, -1))
        elif d < 0 and b["l"] <= lo[i]: sig.append((i, 1))
    return sig
