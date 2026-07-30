# Roadmap

## Thesis (validated by research, 2026)

Build the **risk-and-behaviour layer**, not the trade-idea layer. Ballast answers three questions:

1. **Before the trade** — can I take this without risking the account or the payout?
2. **During the trade** — am I near a rule breach or making a loss-driven decision?
3. **After the trade** — did I show skill and process discipline, or did I get lucky?

Evidence this is the right layer:

- A Brazilian equity-futures study found ~97% of traders active >300 days lost money; ~1.1% earned more than minimum wage (Chague, De-Losso & Giovannetti).
- 15 years of Taiwan data: <1% of day traders reliably earned positive abnormal returns after costs (Barber, Lee, Liu & Odean).
- CBOT professionals with morning losses were ~15% more likely to take unusually high afternoon risk — loss-chasing is not a beginner problem (Coval & Shumway).
- The most active retail households earned 11.4% vs the market's 17.9% — frequency and overconfidence raise costs without raising returns (Barber & Odean).

**Product implication:** a tool cannot manufacture an edge that doesn't exist. Ballast must be willing to say *"you don't yet have enough evidence to pay for another evaluation."* That honesty is the differentiator.

## Design principles

- **The headline number is the failure buffer, not the account size.** A $1,000 stop on a "$100k" account with a $3,000 trailing drawdown is not 1% risk — it's ~33% of everything keeping the account alive. Always show risk as a share of remaining buffer.
- **Deterministic rules decide compliance.** AI may explain a rule; arithmetic decides it.
- **Process is scored before P&L.** A profitable rule-breaking day scores badly, or the software trains the behaviour that kills accounts.
- **Never suggest sizing up to reach a target faster.**
- **Prop rules change constantly** — treat every rule set as versioned, dated, and needing reverification.

## Phase 0 — validate ✅

- [x] Landing page + waitlist (persisted to Postgres)
- [x] Free trailing-drawdown cushion calculator
- [x] disciplineEngine + riskSignals + language layer (rule-based, tested)
- [x] Live session console with auto-play demo of the give-back spiral
- [x] Challenge-readiness check: stats + rule-aware Monte Carlo, incl. the honest "no edge yet — don't buy" verdict
- [x] Risk shown as % of remaining failure buffer
- [x] Deployed at tradeballast.com
- [ ] Publish the launch post, gather first 30+ emails

## Phase 1 — MVP

- [ ] Auth (Auth.js / Clerk) — guide in `docs/AUTH_SETUP.md`
- [ ] Persist trades + accounts (schema already migrated)
- [ ] Account setup: firm, phase, drawdown type (intraday vs EOD), reset time
- [ ] Live next-action card driven by real logged data
- [ ] Behavioural tag analytics — edge by tag (revenge / FOMO / boredom / A+)
- [ ] Daily rule scorecard + clean-day streak

## Phase 2 — charge

- [ ] Stripe Checkout + Billing ($19–29/mo)
- [ ] Feedback learning loop wired to `recommendation_feedback_events`

## Phase 3 — differentiate

- [ ] **Evidence-of-edge engine**: expectancy after commissions and slippage, sample size, confidence range, in-sample vs out-of-sample, largest losing streak, and an explicit "could this be luck?" readout. A green number over 12 trades is not proof.
- [ ] **Escalating intervention ladder**: quiet warning → 10-second confirm → restate the plan → cooldown timer → optional lockout. The user configures whether it may block.
- [ ] **Versioned rulebooks per firm/product**, sourced and dated (e.g. Apex legacy vs current accounts differ — a generic "Apex" preset is unsafe).
- [ ] **Economic truth**: payouts minus every evaluation, reset, activation, platform and data fee — not gross simulated P&L.

## Phase 4 — the moat

- [ ] Broker auto-import (Rithmic / Tradovate / NinjaTrader) — real fills, real time
- [ ] Privacy-safe dataset linking *real-time decisions* (not just completed trades) to account survival and repeat payouts. This is what no journal or prop dashboard has: evidence of which interventions actually make traders safer.

## Explicitly NOT building

AI signal generators · guaranteed "challenge passers" · fastest-to-funded leaderboards · social P&L comparison · motivational chatbots · affiliate-driven prop-firm ranking · black-box order blockers · backtests that ignore slippage, commissions and rule paths. Each increases activity while degrading decision quality.

## Success metric

**Not** challenge pass rate. The north star is:

> % of users who stay rule-compliant for 90 days **and** receive net payouts exceeding all prop-firm and tool costs.

Supporting: first- and repeat-payout rates · account survival at 30/60/90 days · breaches per 100 accounts · fees paid per successful payout · risk taken after a losing trade · unplanned trades per session · stop-widening frequency · expectancy after all costs · and the % of users correctly told their edge is *not* yet established.

---

*Product research, not financial advice. Prop-firm rules change frequently and must be reverified.*
