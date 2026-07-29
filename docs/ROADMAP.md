# Roadmap

## Phase 0 — validate (this repo)
- [x] Landing page + waitlist
- [x] Free cushion calculator (lead magnet)
- [x] disciplineEngine + riskSignals + language (rule-based core)
- [x] Live session console demo (`/session`) — engine reacting to a trade log, in-memory
- [x] Build-in-public launch post drafted (`docs/launch-post.md`)
- [ ] Ship it, start building in public, gather 30+ emails

## Phase 1 — MVP
- [ ] Auth (Auth.js / Clerk) — guide ready in `docs/AUTH_SETUP.md`
- [ ] Manual trade log (form → trades table) — UI exists in `/session`, needs DB persistence
- [ ] Account setup (trailing drawdown, type)
- [x] Live "next action" card driven by disciplineEngine (demo built; wire to real data next)
- [ ] Behavioral tag analytics (edge by tag)
- [ ] Daily rule scorecard + clean-day streak

## Phase 2 — charge
- [ ] Stripe Checkout + Billing ($19–29/mo)
- [ ] Feedback learning loop wired to recommendation_feedback_events

## Phase 3 — the moat
- [ ] Broker auto-import (Rithmic / Tradovate / NinjaTrader)
