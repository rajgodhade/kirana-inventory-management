# Cash Register, Cashier Shift, and Day Closing

Phase 12 adds one physical cash-register session per store without changing Billing or making an open register a prerequisite for a sale. Transactional amounts remain owned by their existing modules and the register report reads those authoritative records.

## Lifecycle

1. An authorized user opens the register with a non-negative opening cash count.
2. Completed sales, customer-credit repayments, and sales returns flow into the current report from their existing tables.
3. Authorized users may record manual Cash In or Cash Out entries. Every entry requires an amount, reason, user, timestamp, and idempotency operation ID.
4. An X Report shows the current totals and optional counted-cash difference without changing the session.
5. Closing requires a physical counted-cash value and explicit confirmation. The operation stores an immutable Z Report snapshot and releases the store for its next session.

Only one `Open` session can exist per store. This is protected both in the service transaction and by a filtered unique SQLite index.

## Expected cash formula

```
Opening Cash
+ Cash Sales
+ Cash Customer-Credit Repayments
+ Manual Cash In
- Cash Refunds
- Manual Cash Out
= Expected Cash
```

UPI, card, and customer-credit sales are reported but never affect the physical drawer. `Payment.Amount` is used for cash sales, so cash tendered and change returned do not overstate the drawer.

## X and Z reports

- X Report: a live calculation for the open session. Viewing it is audited and never closes the register.
- Z Report: the snapshot written during close. It includes sales and tender splits, returns, manual movements, expected cash, actual cash, and variance. Reopening a Z Report never recalculates historical values.

The Cash Register management page supports opening, Cash In, Cash Out, X Report, counted closing, printable Z Report, and session history. Billing displays only a compact open/closed indicator and remains operational if register status cannot be loaded.

## Permissions and audit

- `CashRegister.View`
- `CashRegister.OpenClose`
- `CashRegister.CashIn`
- `CashRegister.CashOut`

Owner and Manager receive all four permissions. Cashier receives View and Open/Close by default; manual movements require Manager or Owner authorization. Service-layer checks protect every mutation and report lookup regardless of UI visibility.

Audited events include register opening and closing, Cash In, Cash Out, X Report viewing, Z Report generation, and Z Report viewing.

## Persistence

- `CashRegisterSessions` contains the lifecycle and the immutable close snapshot.
- `CashMovements` contains manual physical drawer changes.
- Migration: `20260811170000_Phase12CashRegister`.

The application applies pending migrations during startup. Existing installations retain their sales, payments, returns, credit, and audit data.

## Recovery and operational notes

- A process restart does not lose an open register because the session is persisted immediately.
- Retrying a manual movement with the same operation ID returns the existing entry and does not duplicate cash.
- A failed transaction rolls back its register mutation and audit write together.
- If a session was left open accidentally, an authorized user can review the X Report, count the drawer, and close it normally; no sales data needs repair.
