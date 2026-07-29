# Ballast

**The discipline layer for prop futures traders.** Not another journal — an engine that
watches your trailing-drawdown cushion, catches the revenge trade in the moment, and grades
whether you followed your own rules. Built to make it hard to give back weeks of gains in a day.

> **Ballast** — the stabilizing weight that keeps a vessel from capsizing. Scaffolded from the
> proven bones of an existing TypeScript monorepo; see `docs/PROVENANCE.md` for what was lifted.

## Stack
- **apps/web** — Next.js 16 / React 19 (App Router, TypeScript)
- **packages/db** — Postgres + `node-pg-migrate` migrations
- **lib/ai** — OpenAI client
- **infra** — Dockerized Postgres + migration runner

## Quickstart
```bash
cp .env.example .env          # fill DATABASE_URL + OPENAI_API_KEY
npm install                   # installs all workspaces
npm run db:up                 # start Postgres in Docker
npm run db:migrate            # create the schema
npm run dev:web               # http://localhost:3000
```

## What's here today (Phase 0 + engine stub)
- Landing page with a waitlist (`/`) — start gathering interested traders.
- Free trailing-drawdown **cushion calculator** (`/cushion`) — no signup, your lead magnet.
- **`apps/web/lib/disciplineEngine.ts`** — the brain: signals → one next action → plain-language
  reason → urgency. Rule-based and fully explainable. This is the differentiator.
- Full trading schema: accounts (with trailing-drawdown fields), trades, rules, daily grades,
  a recommendation-feedback learning loop, and waitlist.

## What's next (see docs/ROADMAP.md)
1. Auth (Auth.js or Clerk) — the users table exists; login does not.
2. Stripe self-serve subscriptions.
3. Manual trade log + the daily rule scorecard UI on top of the engine.
4. Broker auto-import (Rithmic / Tradovate / NinjaTrader) — the moat.

Not financial advice.
