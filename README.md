# Ballast

Ballast is an advisory discipline and drawdown-monitoring layer for prop futures
traders. It combines a NinjaTrader 8 add-on with a Next.js website and waitlist.
It does not place, change, cancel, or flatten orders.

## Repository

- `ninja/Ballast` - NinjaTrader add-on, risk engine, fill ledger, and local journal.
- `ninja/test` - NinjaTrader-independent C# regression suite.
- `apps/web` - Next.js website, cushion calculator, and waitlist API.
- `packages/db` - Postgres migrations.
- `scripts` - repeatable C# test and real-NinjaTrader compile gates.

## Development

```powershell
npm install
npm run test:web
npm run lint:web
npm run build:web
npm run test:ninja
npm run compile:ninja-real  # requires NinjaTrader 8 installed locally
```

The web waitlist expects the schema to be migrated before requests arrive. Remote
Postgres connections verify TLS certificates; use `DATABASE_SSL_CA_BASE64` for a
private CA. Production traffic still needs an edge-enforced rate limit because the
route's in-process limiter is only a local backstop.

## Trust boundary

Ballast persists account-lifetime intraday peaks, completed-session end-of-day
anchors, and per-account firm reset times. Where the connection supplies
NinjaTrader's remaining trailing-max-drawdown value, Ballast validates and persists
the provider-derived firm threshold automatically. Executions are journaled from
immutable callbacks per instrument, with duplicate suppression and fail-closed
position reconciliation. Durable local files use atomic replacement and backups.

Connections that do not transmit trailing-max-drawdown data still require the
firm dashboard to reconcile an older account's unobserved history. Signed remote
rule updates remain disabled until a production signing key and verifier are
provisioned.

See [ninja/INSTALL.md](ninja/INSTALL.md) for installation and validation details.

Not financial advice.
