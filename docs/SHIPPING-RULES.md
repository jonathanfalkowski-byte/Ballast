# Before anything reaches a live account

Written on 11 August 2026, after two changes in one morning broke figures a
trader was relying on while he was trading. Both were mine, both were caught by
him rather than by me, and both had the same shape.

## What actually went wrong

Neither bug was a logic error in fresh state. Both were **state restored from
disk**.

1. A feed carried its realised P&L across a day boundary, so yesterday's
   $1,357 loss was read as today's and the daily-loss wall went up on a morning
   he had not opened the platform.
2. The fix for it compared today's saved figure against itself on a mid-session
   restart, decided the feed was carrying, and baselined the day from it. Three
   accounts showed the right trade counts beside zeroed P&L.

Every test passed both times. The tests all started from a fresh tracker.

## The rules

**1. Anything touching persisted state, the session baseline or the day
boundary gets a restart test.**

Not "a test". A test that *seeds from a saved session and then asserts the day
survived*. The pattern:

```
build a day, trade it, assert the figures
→ construct a NEW tracker
→ SeedSession(...) with what the file would hold
→ EnsureSession(...) / OnEquity(...)
→ assert the figures are still right
```

If a change can alter what is written to `ballast-session.txt`,
`ballast-journal.csv` or `ballast-settings.txt`, or how any of it is read back,
it is in this category. So is anything touching `EnsureSession`,
`ApplySessionSeed`, `ApplySeed`, `sessionStartRealised`, `LastClosingDailyPnl`
or `TrustAccountRealised`.

**2. Say which category a change is in, before he compiles.**

- *Cosmetic* — layout, wording, colour. Compile whenever.
- *Behavioural* — what Ballast says or when. Compile between sessions.
- *State* — anything under rule 1. Compile when flat, and check the Now page
  against the platform's own Accounts tab before trading.

**3. Do not ship state changes into a live session.**

Both of today's incidents landed while he was trading. A wrong cushion during a
session is worse than a missing feature for a day.

**4. When a fix repairs bad data, it has to repair data that is ALREADY bad.**

A fix that only stops new bad values leaves everyone holding the old one. The
day's figures are checked against something that cannot be carried - the trade
count - so a stuck day heals itself rather than needing the file edited.

## The standing check

Before saying a change is ready:

- does it change anything on disk, or anything read from disk?
- is there a test that restarts and asserts the day survives?
- has the whole suite run, from the repo copy, after the files were staged?
- which category is it, and is he trading right now?
