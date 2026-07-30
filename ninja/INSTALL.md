# Ballast for NinjaTrader 8 — install & first run

**Advisory only.** This add-on never places, modifies or cancels an order, and never
flattens a position. It reads your account and tells you what it sees. That is a
deliberate v1 decision — software that closes positions can cost real money when
it's wrong, and this hasn't earned that trust yet.

## Install

1. Close NinjaTrader if it's open.
2. Copy the whole `Ballast` folder into:
   ```
   Documents\NinjaTrader 8\bin\Custom\AddOns\
   ```
   So you end up with `...\AddOns\Ballast\DisciplineEngine.cs` and the other three files.
3. Start NinjaTrader.
4. Open the **NinjaScript Editor** (Control Center → New → NinjaScript Editor).
5. Press **F5** to compile.
6. If it compiles cleanly, open it from **Control Center → New → Ballast**.

If compilation fails, copy the error text and send it over — the engine and tracker
are unit-tested, but the NinjaTrader UI layer was written without being able to
compile against your NinjaTrader assemblies, so a small API mismatch on first
compile is the likely failure and it's quick to fix.

## First run

1. Pick your account in the dropdown at the top.
2. Fill in **your** rules:
   - **Account size** and **trailing drawdown** — the real numbers from your firm.
   - **Drawdown type** — you told me yours is **intraday trailing**, so leave it on that.
     (Intraday means the floor follows your *peak* equity, including unrealised profit.)
   - Stop-after-N-losses, daily loss limit, daily target, max trades, max contracts.
3. Click **Apply and save**. Settings persist across restarts.

Then just leave the window open next to your DOM.

## Choosing your firm and account type

Under **Prop firm account type**:

1. Pick your **Firm** — Apex Trader Funding, Topstep, Take Profit Trader,
   MyFundedFutures (more can be added, see below).
2. Pick your **Account type** — e.g. `Trading Combine - 50K`, `PRO (funded) - 100K`,
   `Rapid (4% intraday) - 150K`.
3. Then either:
   - **Apply to selected** — sets the account currently chosen under "Editing rules for".
   - **Auto-match all by balance** — reads every monitored account's balance and
     matches each to the right size for that firm. One click for twenty accounts.

Detection fills in the drawdown, drawdown type, daily loss limit and profit target.
Your personal guardrails — stop-after-N-losses, max trades, max contracts — are
never overwritten.

It refuses to guess: if a balance matches no standard size for that firm, it says so
rather than inventing a number.

## Rules keep themselves up to date

You never maintain the rule book. When the window opens, Ballast quietly asks
tradeballast.com whether a newer rule book exists and installs it if so — at most
once a day. There's also a **Check for rule updates** button if you want to force it.

How the whole chain works:

1. A scheduled job re-checks the firms' own rules pages every week.
2. If a firm has changed something, the canonical rule book on the server is corrected.
3. Every add-on picks that up automatically on its next check.

So when Apex or Topstep move a drawdown, your cushion figure follows without you
lifting a finger.

**It is built to never get in your way.** The check runs on a background thread with
a short timeout — a slow or dead connection cannot touch the trading UI. If the
download fails, your existing cached rules keep working, offline included. A payload
that doesn't parse is rejected rather than installed, and a lower version number is
ignored so you can't be silently downgraded.

If you do want to look, the current rules cache is `ballast-rules.txt` beside the
code, and it's still editable — your edits simply get replaced next time the server
publishes a newer version.

## Testing on Sim104

Sim accounts appear in the list like any other. A Sim account has no real firm, so
pick whichever firm and account type you want to simulate and hit **Apply to selected** —
Ballast will then treat Sim104 as if it were that account. Trade it and watch the
counters move.

## What you should see

- **Can lose before blown** — dollars between you and a closed account. This is the
  number that matters, not your account size. Where a firm freezes the trailing
  drawdown past a threshold, the row is marked *(locked)* and the floor stops
  following you up.
- **Day P&L**, **Losses**, **Trades** — counted automatically from your fills.
- **The card** — one action, colour-coded:
  - green *Clear to trade*
  - amber *Size down* / *Hold*
  - red *Step away* (you're inside the tilt window after a loss)
  - red *Stop* (max losses hit)
  - red *Lock out* (daily loss limit hit)
  - red *Protect it* (you're handing back a green day)

## The journal

You do not fill it in. Every round-trip is recorded the moment you go flat, with
the instrument, direction, size, entry and exit times, duration and P&L taken
straight from NinjaTrader. Nothing to look up, nothing to type.

It also records the part no other journal has: **what Ballast was advising at the
moment you opened the trade**, your cushion at entry, how many minutes it had been
since your last loss, and which trade of the day it was. That is captured whether
or not you ever touch a button, which means Ballast can tell you something true
even if you never tag a single trade — for example, *"11 trades were opened after
Ballast said stop, and together they cost $2,400."*

### What it asks you for

Two things software cannot see, and only these:

- **Planned or not planned.** One tap. This is the whole point: it grades the
  *decision*, not the result, so a good trade that lost and a reckless trade that
  won get scored honestly.
- **A feeling**, picked from a fixed list — Calm, Rushed, Wanted it back, Afraid to
  miss it, Bored, Unsure. Picked, never typed. Selecting a label from a list
  produces an immediate drop in emotional intensity; writing your own wording only
  helps days later and can make you feel worse first. It is also one click instead
  of a sentence, and friction is what kills journals.

Finished trades queue in an amber strip near the top of the window. It never pops
up over your charts and never steals focus — a tool that interrupts you mid-decision
would be doing harm in order to sell discipline. Tag them whenever you glance over,
or clear the lot at the end of the session. Nothing is mandatory.

### Today's plan

One line, written as **"if X, then I will Y"**. For example: *"If I lose two trades,
then I close NinjaTrader for the day."*

This is not a motivational exercise. A vague intention barely moves behaviour; a
specific if-then plan reliably does, because it hands the decision to the situation
rather than to your willpower at the moment it is weakest. Ballast stamps whatever
you write onto every trade you take that day, so at the end of the month you can see
which plans you actually kept.

The plan does not carry over to the next day, on purpose. A plan you didn't write
this morning is one you haven't committed to.

### Where it lives

`Documents\NinjaTrader 8\ballast-journal.csv` — plain CSV, saved continuously.
Open it in Excel and add any columns you like; Ballast only ever rewrites the
columns it owns. Your journal is yours, and it is never locked inside this tool's
own format.

## How it counts a trade

One trade = one round-trip. It starts when you go from flat to a position, and
completes when you're flat again. Scaling in and out counts as **one** trade, not
several. The trade's P&L is the change in realised P&L across that round-trip, so
a loss is detected the moment you close red.

## Known limits of v1

- Counts a net position across all instruments on the account. If you trade two
  instruments simultaneously, round-trip detection will be approximate.
- Session state resets on date change (exchange time), not on your firm's specific
  daily reset time.
- It reads `CashValue + UnrealizedProfitLoss` as equity. On some prop/sim account
  types this may differ from what your firm's dashboard shows — check it against
  your firm on day one before trusting the cushion figure.
- Advisory only, by design. No lockout enforcement yet.

## What to tell me after the first session

1. Did it compile?
2. Did the trade/loss counts match what actually happened?
3. Did the cushion figure match your firm's dashboard?
4. Did the card ever fire when you were genuinely about to do something stupid —
   and did it change what you did?

That last one is the only question that really matters.
