# Provenance — lifted from Velocity

Ballast reuses the *engineering patterns* of the Velocity monorepo (an insurance
decision/execution product), not its domain code. Velocity is left untouched; this is a clean,
separate repo so the high-stakes project can't be destabilized by a side project.

## Lifted ~as-is
- Monorepo layout + npm workspaces (`apps/*`, `packages/*`)
- `packages/db` migration setup (`node-pg-migrate`, CommonJS `pgm` migrations, `gen_random_uuid()`,
  `tenant_id` FKs, `timestamptz` defaults)
- `infra/runMigrations.js` runner + Dockerized Postgres
- `lib/ai/openaiClient.js` pattern
- Tailwind + PostCSS config
- `tenants` + `users` multi-tenant model

## Adapted (same architecture, re-skinned for trading)
- `decisionEngine.ts`  →  `apps/web/lib/disciplineEngine.ts`
- `riskSignals.ts`      →  `apps/web/lib/riskSignals.ts`
- `decision-language.ts` (the "<Action> — <Reason>" headline) → `apps/web/lib/decisionLanguage.ts`
- `recommendation_feedback_events` learning loop → same table, trading context

## Built new (not in Velocity)
- Trading data model: `accounts`, `trades`, `rules`, `daily_grades`
- Phase 0 landing + waitlist
- Free cushion calculator
- (TODO) real auth + Stripe billing
