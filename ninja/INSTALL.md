# Ballast for NinjaTrader 8 - install and first run

Ballast is advisory software. It never places, changes, cancels, or flattens an order.

## Install

1. Close NinjaTrader.
2. Copy the entire `ninja/Ballast` folder to
   `Documents\NinjaTrader 8\bin\Custom\AddOns\Ballast`.
3. Start NinjaTrader and open Control Center -> New -> NinjaScript Editor.
4. Press F5. Fix every compile error before opening Ballast.
5. Open Control Center -> New -> Ballast.

Repository checks:

```powershell
npm run test:ninja
npm run compile:ninja-real
```

The second command needs a local NinjaTrader 8 installation and checks the code
against the real NinjaTrader assemblies. It does not replace the F5 compile.

## Configure every account

Under Setup, select each account and verify:

- account starting balance, trailing drawdown, and intraday or end-of-day drawdown;
- firm lock level and account generation;
- personal daily loss, target, loss count, trade count, and contract limits;
- `Firm trading day resets`, using NinjaTrader's configured platform clock;
- whether the account's realized P&L resets each session;
- the optional personal trading window.

Click Apply and save. Account settings are independent and persist across restarts.
Profile matching is a starting point, not proof of the firm's current rules.

## Automatic PA floor recovery

When the connected provider supplies NinjaTrader's `Trailing max drawdown` account
value, Ballast converts that remaining room into the actual firm threshold, checks
it against the selected account profile, and marks the cushion `firm`. The trader
does not enter a historical peak or liquidation floor. The confirmed threshold is
saved locally and never moves downward.

If the provider does not transmit that account value, Ballast falls back to its
persisted high-water history and the firm's published formula. On the first day,
compare the displayed floor with the firm dashboard; provider support varies by
connection.

## Execution journal

Live execution callbacks drive the journal. Partial fills, scale-outs, reversals,
and simultaneous instruments are kept in separate fill ledgers. A position that
was already open when Ballast attached is explicitly marked approximate. If the
execution ledger and NinjaTrader positions disagree after the reconciliation grace
period, Ballast fails closed and shows a lockout warning.

State and journals are written by atomic replacement with a backup of the previous
complete file. The journal remains local at
`Documents\NinjaTrader 8\ballast-journal.csv`.

## Rule updates

The bundled rule book remains usable offline. Remote updates are rejected unless
the response is signed, complete, structurally valid, current, and newer than the
installed version. This source tree intentionally has no embedded trust-on-first-use
key: automatic updating stays disabled until the production signing verifier and
server-side signer are provisioned. Never weaken this gate to accept unsigned data.

## Current release boundary

- Advisory only; there is no order-blocking enforcement.
- A connection that does not transmit trailing-max-drawdown data cannot resolve an
  older account's unobserved high-water state automatically.
- Run a full Sim account soak, including restart, partial fill, reversal, overnight
  reset, and disconnect/reconnect scenarios before using it beside a live account.
- Firm rules change. Verify profile values with the firm's official rules.
