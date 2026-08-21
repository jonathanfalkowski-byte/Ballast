"""
Collapse NinjaTrader tick exports into 1-second bars carrying TRUE bid/ask volume delta.

Input line format (NinjaTrader "Last" tick export, timestamps in UTC):
    yyyyMMdd HHmmss ffffff;last;bid;ask;volume

Trade classification (possible because the export carries the quote at trade time):
    last >= ask  -> buyer-initiated  (lifted the offer)
    last <= bid  -> seller-initiated (hit the bid)
    otherwise    -> neutral (mid / locked market), counted but not signed

Two delta series are produced:
    delta      - every trade
    fdelta     - only trades with SIZE_MIN <= size <= SIZE_MAX, mirroring the
                 ninZa Quantum Vol-Delta "Volume Filter" (min 3, max 999)

Output: one CSV row per second that had at least one trade, restricted to the
RTH window the trader actually trades. Downstream code aggregates these into
30-second (or any other time-based) bars without re-reading the ticks.
"""
import sys, os, csv

SIZE_MIN, SIZE_MAX = 3, 999

# US DST 2026: EDT runs Mar 8 -> Nov 1. Files are UTC, so the ET session
# window lands on a different UTC hour either side of that date.
DST_START, DST_END = "20260308", "20261101"

def utc_window(datestr, et_start_min, et_end_min):
    """ET minutes-from-midnight -> UTC minutes, accounting for DST."""
    off = 4 if DST_START <= datestr < DST_END else 5
    return et_start_min + off * 60, et_end_min + off * 60

def run(paths, out_path, et_start_min, et_end_min):
    out = open(out_path, "w", newline="")
    w = csv.writer(out)
    w.writerow(["date","sec_utc","open","high","low","close",
                "vol","buy","sell","neut","delta","fvol","fdelta","ticks"])

    cur_key = None
    o = h = l = c = 0.0
    vol = buy = sell = neut = fvol = fdelta = ticks = 0
    kept = scanned = 0

    for path in paths:
        # Cache the per-day UTC window so we don't recompute it per tick.
        day = None
        lo = hi = -1
        with open(path, "rb") as fh:
            for raw in fh:
                scanned += 1
                if len(raw) < 20:
                    continue
                d = raw[0:8]
                if d != day:
                    day = d
                    ds = d.decode()
                    lo, hi = utc_window(ds, et_start_min, et_end_min)
                # HHMM at bytes 9..12 -> minutes from midnight, cheap reject
                try:
                    mins = (raw[9]-48)*600 + (raw[10]-48)*60 + (raw[11]-48)*10 + (raw[12]-48)
                except IndexError:
                    continue
                if mins < lo or mins >= hi:
                    continue

                parts = raw.split(b";")
                if len(parts) != 5:
                    continue
                try:
                    last = float(parts[1]); bid = float(parts[2])
                    ask = float(parts[3]);  size = int(parts[4])
                except ValueError:
                    continue

                sec = raw[9:15]                       # HHMMSS
                key = (d, sec)
                if key != cur_key:
                    if cur_key is not None:
                        w.writerow([cur_key[0].decode(), cur_key[1].decode(),
                                    o, h, l, c, vol, buy, sell, neut,
                                    buy - sell, fvol, fdelta, ticks])
                        kept += 1
                    cur_key = key
                    o = h = l = c = last
                    vol = buy = sell = neut = fvol = fdelta = ticks = 0

                if last > h: h = last
                if last < l: l = last
                c = last
                vol += size
                ticks += 1

                # Locked/crossed quote carries no information about aggressor.
                if bid >= ask:
                    signed = 0
                elif last >= ask:
                    signed = 1
                elif last <= bid:
                    signed = -1
                else:
                    signed = 0

                if signed > 0:   buy += size
                elif signed < 0: sell += size
                else:            neut += size

                if SIZE_MIN <= size <= SIZE_MAX:
                    fvol += size
                    fdelta += signed * size

        print(f"  done {os.path.basename(path)}  scanned={scanned:,} kept_secs={kept:,}",
              flush=True)

    if cur_key is not None:
        w.writerow([cur_key[0].decode(), cur_key[1].decode(),
                    o, h, l, c, vol, buy, sell, neut,
                    buy - sell, fvol, fdelta, ticks])
        kept += 1
    out.close()
    print(f"TOTAL scanned={scanned:,}  seconds written={kept:,}  -> {out_path}", flush=True)

if __name__ == "__main__":
    # Widened past the 9:30-12:00 trade window so indicator warmup and
    # after-window trade exits both have data.
    run(sys.argv[2:], sys.argv[1], 8*60, 13*60)
