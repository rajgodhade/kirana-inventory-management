# Cash Register, Cashier Shift, and Day Closing

One physical cash-register session per store. Transactional amounts remain owned by their existing modules and the register report reads those authoritative records rather than copying them.

Phase 12 deliberately made the register observe-only: an open register was never a prerequisite for a sale. Phase 16A-2 changed that for **cash** specifically — see [Cash transactions require an open register](#cash-transactions-require-an-open-register-phase-16a-2). Non-cash billing is still completely independent of the register.

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
- Supplier Cash Payments
- Cash Expenses
= Expected Cash
```

UPI, card, and customer-credit sales are reported but never affect the physical drawer. `Payment.Amount` is used for cash sales, so cash tendered and change returned do not overstate the drawer. The same rule governs the two outflows derived from other modules: only `PaymentMethod.Cash` supplier payments and expenses move physical money.

## Cash expenses (Phase 16A-1)

An expense paid in cash reduces the drawer on its own. Like supplier cash payments, it is **derived from its own table and never mirrored into a `CashMovement`** — the expense row stays the single financial record, so there is nothing to double-count, duplicate on retry, or leave orphaned by a half-failed write. A cash expense and a manual Cash Out of the same amount are two different events and both reduce the drawer.

### Membership uses `CreatedAtUtc`, not `ExpenseDateUtc`

This is the one rule that makes cash expenses different from every other derived figure.

| Field | Meaning | Editable |
| --- | --- | --- |
| `ExpenseDateUtc` | the **accounting** date — which day the cost belongs to | yes, freely, including backdating |
| `CreatedAtUtc` | when the record, and therefore the cash, actually left | no; set once by the `Entity` base |

Every other source keys on a server-stamped event time. `SupplierPayment.PaymentDateUtc`, for instance, is always the moment of recording. An expense is not like that: its date is a user-editable field with a date picker, so a bill from last Tuesday can legitimately be entered today. The cash still left the drawer *today*.

Keying on the accounting date would therefore let an operator move money in and out of a reconciled session just by editing a date field. Membership uses `CreatedAtUtc` instead:

```
Register opens Monday 9:00 AM
Expense: ExpenseDateUtc = last Tuesday, CreatedAtUtc = Monday 11:00 AM, Cash
  → belongs to Monday's register
```

### Closed sessions are protected

A closed session's Z report records what a human physically counted. Because cash expenses feed `ExpectedCash`, `ExpenseService` **refuses to edit or delete a cash-affecting expense whose `CreatedAtUtc` falls inside a closed session**, with a message naming the register and its business date. The refusal covers amount changes, method changes and deletion, and it applies in both directions — a non-cash expense in a closed window also cannot be flipped *to* cash, which would inject money into a frozen session.

Non-cash expenses inside a closed window remain freely editable: they never touched the drawer, so no reconciliation depends on them.

### Historical sessions are not backfilled

The `CashExpenses` column defaults to `0` and **no historical session is recalculated**. Sessions closed before this feature existed report `0`, which means "the cash-expense concept was not part of this snapshot" — not "no cash was spent". Backfilling would retro-subtract expenses from `ExpectedCash` and so change a `Variance` that a human already counted and signed off, which is precisely what a Z report must never do.

This gap — a cash expense belonging to no session — is closed by the policy below.

## Cash transactions require an open register (Phase 16A-2)

Money cannot move in or out of a drawer that does not exist. Since 16A-2, a transaction that moves **physical cash** is refused unless a register is open; a transaction that does not is unaffected.

| Transaction | Register required? |
| --- | --- |
| Cash sale | **yes** |
| Split payment containing **any** cash tender | **yes** |
| Cash refund on a sales return | **yes** |
| Cash udhaar repayment | **yes** |
| Cash supplier payment (including the up-front payment on a purchase) | **yes** |
| Cash expense | **yes** |
| Manual Cash In / Cash Out | **yes** (unchanged — always did) |
| UPI / Card / Udhaar sale | no |
| UPI + Card split | no |
| Store-credit or `None` refund | no |
| Non-cash repayment, supplier payment or expense | no |
| Purchase recorded entirely on credit | no |

This reverses the Phase 12 statement above that an open register is never a prerequisite for a sale. That was true when the register only observed; now that it reconciles, an unreconcilable cash sale is worse than a refused one.

### One definition, enforced at the trust boundary

`CashImpactPolicy` is the only place that decides what "cash-impacting" means. The rule is **any tender is cash**, not "the tender is cash" — split payments are supported, so a Cash + UPI bill still opens the drawer. Refunds answer through a separate `RefundMethod` enum, which the policy overloads deliberately: the two enums are easy to confuse, and a refund path reaching for `PaymentMethod` would not compile.

Enforcement lives in the **services** — `SaleService`, `SalesReturnService`, `CustomerCreditService`, `PurchaseService`, `ExpenseService` — before any financial mutation. The POS checks the same policy first, but that is a convenience: it produces the message a moment sooner and saves a round trip. **A request built by hand, or a client with the check patched out, is refused exactly the same way.** UI validation is never the thing standing between a cash sale and an unreconciled drawer.

The check reads the register with `AsNoTracking()`, so a register closed on another screen is seen immediately rather than through a stale identity map.

### What it does not do

- **No queueing.** A refused transaction is refused, not held for the next session.
- **No automatic register creation or reopening.** Opening cash is a physical count by a human; a machine-created session would fabricate that number.
- **No assignment to a future session.** That session's opening cash was counted without this money in the drawer.
- **No historical rewrite.** The policy governs new transactions only. Existing sessions, Z reports and transactions are untouched, and nothing was backfilled.
- **No new permission.** A user allowed to sell is still refused a cash sale with no open register; permissions and the register policy are independent gates and neither bypasses the other.

### Failure behaviour

The refusal happens before anything is written, so nothing partial survives: no Sale, SaleItem, Payment, stock movement, inventory change or audit row for a rejected cash sale, and likewise for the other four paths. In the POS the cart and every payment line stay exactly as they were — the cashier opens the register and returns to the same bill.

The message is always: *"No cash register is open. Open the register before processing cash transactions."*

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
- `CashMovements` contains manual physical drawer changes only — never supplier payments or expenses.
- Migrations: `20260811170000_Phase12CashRegister`, `20260812090000_AddSupplierCashPaymentsToRegister`, `20260816120000_AddCashExpensesToRegister`.

The application applies pending migrations during startup. Existing installations retain their sales, payments, returns, credit, and audit data.

## Recovery and operational notes

- A process restart does not lose an open register because the session is persisted immediately.
- Retrying a manual movement with the same operation ID returns the existing entry and does not duplicate cash.
- A failed transaction rolls back its register mutation and audit write together.
- If a session was left open accidentally, an authorized user can review the X Report, count the drawer, and close it normally; no sales data needs repair.
