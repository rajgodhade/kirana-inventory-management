# Inventory Adjustments (Phase 13D)

Phase 13D lets an authorized user correct stock when the quantity is wrong for a reason that is
**not** a sale, purchase, return, or physical stock count: damage, expiry, loss, theft, found goods,
or fixing a number that was simply entered wrong.

```
Select Product
      ↓
Current Quantity          ← read fresh, never from the screen
      ↓
Enter Adjustment          ← direction + positive magnitude
      ↓
Select Reason
      ↓
Enter Notes
      ↓
Preview New Quantity      ← informational only
      ↓
Authorize                 ← explicit confirmation
      ↓
Atomic Inventory Update
      ↓
StockMovement
      ↓
Audit
```

## Why manual adjustments exist

Real stock drifts from system stock for reasons no transaction records: a bottle breaks, a packet
expires on the shelf, someone takes something. Without an authorized way to correct that, the
alternatives are worse — either the number stays wrong forever, or somebody edits the database.

## The fundamental rule: nothing silently overwrites stock

There is no "set quantity to X" operation anywhere in this feature. Every change states a direction,
a magnitude and a reason, and produces **three linked records in one transaction**:

| Record | Answers |
| --- | --- |
| `InventoryAdjustment` | *why*, by whom, from what to what |
| `StockMovement` | the ledger entry |
| Audit row | the append-only trail |

Inventory cannot move here without all three.

## Stock count vs manual adjustment

These are deliberately **not** merged, and their movements stay distinguishable in the ledger:

| | Phase 13C — Stock Count | Phase 13D — Manual Adjustment |
| --- | --- | --- |
| Evidence | Someone counted the shelf | Someone asserts a number |
| Trigger | Variance between physical and system | An explicit correction |
| Reason | Implicit ("this is what was there") | **Required**, from a controlled list |
| Movement types | `StockCountIncrease` / `StockCountDecrease` | `InventoryAdjustmentIncrease` / `InventoryAdjustmentDecrease` |
| Reference | `STK-COUNT-000001` | `ADJ-000001` |

A stock-take is evidence; a manual adjustment is a claim. They carry different weight in a shrinkage
investigation, which is exactly why they get different movement types. **Phase 13C behaviour is
unchanged by this phase.**

## Direction and quantity

Callers pass a **direction plus a positive magnitude**, never a signed number:

```
Direction: Decrease     not     Adjustment: -5
Quantity:  5
```

A bare `-5` is easy to mis-key as `5` and silently do the opposite of what was meant — the sign is
precisely the part a tired operator gets wrong. `AdjustmentQuantity` is always stored positive;
`Direction` carries the sign, and `SignedQuantity` combines them.

Rejected: zero, negative magnitudes (that expresses direction twice), and values that round away to
nothing at the schema's 3-decimal precision. All arithmetic is `decimal`; there is no floating point
anywhere in this feature.

## Reasons

A controlled enum, not free text — "how much do we lose to damage vs theft" is only answerable if the
reason is a value you can group by:

`Damaged` · `Expired` · `Lost` · `TheftOrShrinkage` · `Found` · `DataCorrection` · `OpeningBalance` · `Other`

`Lost` and `TheftOrShrinkage` are separate because one is misplacement and the other is an
accusation; merging them would dilute shrinkage reporting.

**Notes** are optional for standard reasons and **required for `Other`**, which says nothing on its
own and would otherwise become an unexplained catch-all. Whitespace does not count as an
explanation — notes are trimmed, and a blank string is stored as null.

## Negative stock is impossible

A decrease that would drive stock below zero is refused **by the service**, not the UI:

```
Current  = 3
Decrease = 5      → rejected, nothing written at all
```

The refusal leaves no adjustment record, no movement, no quantity change and no audit row. Negative
stock is not a display problem — it corrupts valuation and every downstream report.

Decreasing to exactly zero is allowed.

## Concurrency

**The service never trusts the quantity the screen was showing.** Inside the transaction it re-reads
current stock with `AsNoTracking()` and computes from that:

```
Screen showed  : 100
A sale happens : 98
Operator submits: decrease 5

Result: 93        (98 − 5)
NOT     95        (100 − 5, which would silently restore the 2 sold units)
```

> **Why `AsNoTracking` is load-bearing.** EF's identity map returns an already-tracked entity if the
> context has one, so a form that has been open for a while holds a stale quantity and a plain
> requery silently returns it. Phase 13C shipped exactly that bug: every same-context test passed
> while the protection was a no-op against a real database. The regression tests here mutate stock
> through a genuinely **separate `DbContext`**, which is the only way to reproduce it.

Two managers adjusting the same product both land — `100 − 5 − 3 = 92`, with the second movement
recording `PreviousQuantity = 95`. Neither update is silently lost.

## Atomicity

One explicit transaction covers authorization, the fresh read, validation, the adjustment record,
the stock movement, the quantity update and the audit write. Any failure rolls back everything.

There can never be: inventory changed without a movement, a movement without an adjustment record,
an adjustment without an audit row, or a partial update.

This follows `StockCountService` and `CashRegisterService` rather than the codebase's usual
single-`SaveChangesAsync` pattern, because the audit write goes through a separate service — and a
crash between the stock write and the audit write would leave inventory changed with no record of
why. Proven by injecting failures into a decorating `IKiranaDbContext` and a throwing
`IAuditLogger`, not assumed.

## Stock movements

Every adjustment movement records:

```
ProductId        the adjusted product
MovementType     InventoryAdjustmentIncrease / InventoryAdjustmentDecrease
QuantityChange   signed, computed from live stock
PreviousQuantity stock immediately before
NewQuantity      stock immediately after
ReferenceType    "InventoryAdjustment"
ReferenceId      e.g. "ADJ-000001"
Reason           the reason label, e.g. "Damaged"
UserId           who authorized it
TimestampUtc     when
```

**Damage recorded here deliberately does NOT use `StockMovementType.Damaged`.** That type is written
by the sales-return flow and feeds the damaged-stock report; reusing it would mix
goods-returned-broken with shelf breakage and make both figures untrustworthy.

The legacy `PositiveAdjustment` / `NegativeAdjustment` types remain in the enum so historical rows
keep their meaning, but nothing writes them any more.

## Permissions

**No new permission key.** Manual adjustment changes stock levels, which is exactly what
`PermissionKeys.InventoryManage` already governs.

- **Owner** — allowed
- **Manager** — allowed (holds `InventoryManage` by default)
- **Cashier** — denied; navigation hidden **and** direct service calls refused

Enforcement is at the service layer, so bypassing the UI does not bypass the check. A null user
(Billing Mode runs logged out) is refused too. Reads and previews are not gated — the write is the
only privileged operation.

No second authentication mechanism was introduced: there is no adjustment-value threshold requiring
PIN step-up, because the PRD specifies none and inventing a policy would be guesswork. The existing
Phase 4/6 PIN infrastructure remains available if a threshold is ever specified.

## Immutability

A completed adjustment **cannot be edited, deleted, or re-finalized**. The service exposes no such
operation — there is nothing to call.

A mistake is corrected with a **compensating adjustment**:

```
ADJ-000001   -5   Damaged            ← the error, kept
ADJ-000002   +5   DataCorrection     ← the fix
```

Both survive in the ledger. Erasing the first would hide that a mistake was made, which is precisely
what an audit trail exists to prevent.

## Barcode behaviour

Scanning reuses the Phase 13B pipeline (`IBarcodeLookupService`), so **any active barcode** of a
product selects that product, and retired codes or inactive products are refused by exactly the same
rule the POS uses.

A barcode identifies the product **only** — it never implies a quantity. There is no
packaging-specific barcode→quantity conversion, and adding one is explicitly out of scope.

## Units

Adjustments are always expressed in the product's stocking unit (`Product.Unit`), stored at the
schema-wide `(18,3)` precision and rounded on the way in so the stored magnitude and the arithmetic
agree exactly. Decimal quantities work for units that support them (`12.5 Kg`, `8.75 Litre`).

Phase 13A pack conversion is a purchase-side concern and is not applied here.

## Batch / expiry

**Adjustments are product-level, not batch-level** — the same documented limitation as Phase 13C.

`Inventory.QuantityOnHand` is the authoritative stock figure; `ProductBatch.Quantity` is
supplementary detail. There is no existing batch-allocation logic that decides which batch a decrease
should come from, and inventing one here — silently draining the oldest batch, say — would produce
confident-looking batch numbers nobody verified. Batch quantities are left untouched.

## Audit

Every completed adjustment writes an `InventoryAdjusted` entry containing enough to reconstruct the
change without joining anything:

```
Action        : InventoryAdjusted
Entity/Id     : InventoryAdjustment/5
PreviousValue : 56
NewValue      : ADJ-000005: PRD-000020 Amul Butter 100g -3 Piece (56 -> 53), reason Damaged
Reason        : <notes, or the reason label when there are no notes>
UserId        : who authorized it
TimestampUtc  : when
```

A rejected adjustment — unauthorized, or negative-stock — writes nothing, because the transaction
rolls back before the audit step. Audit history is never modified.

## Reports

`IInventoryReportService.GetStockCorrectionSummaryAsync` extends the existing inventory reporting
(same `ReportsView` gate) rather than adding a parallel system. It separates physical stock counts
from manual adjustments, breaking the latter down by reason, so an investigator can tell "found by
counting the shelf" apart from "asserted by hand".

Totals are derived from the `StockMovement` rows the adjustments actually produced, not recomputed
from the records — so the report can never disagree with the ledger. No sales or profit formula is
changed by this phase.

## Replacing the old adjust-stock dialog

Phase 13D **replaced** an earlier lightweight "Adjust Stock" dialog on the Products page. That dialog
wrote real stock movements but had no negative-stock guard, no transaction, no reason, no adjustment
record, and computed against whatever quantity the grid happened to be showing.

The Products row menu's **Stock** action now opens this workflow with the product preselected, so the
convenient entry point survives while the weaker path does not. `ProductsViewModel.AdjustStockAsync`
was removed for the same reason: leaving a shortcut would have kept the old behaviour reachable.

`IInventoryService.AdjustStockAsync` remains as the low-level primitive for existing/back-compat
callers, but nothing in the UI reaches it any more.

## Known limitations

- **Product-level only, not batch-level** — see Batch / expiry above.
- **No adjustment-value threshold or PIN step-up.** Any user with `InventoryManage` may adjust any
  quantity. A threshold policy was not invented because the PRD specifies none.
- **No bulk adjustment.** One product per adjustment, deliberately — a bulk "set these 40 products"
  screen is much closer to a stock count, which already exists.
- **No pack-barcode quantity conversion.** A scan identifies the product, never a quantity.
- **No approval workflow.** An adjustment applies immediately on confirmation; there is no
  submitted-then-approved state.
- **Compensating adjustments, not reversals.** There is no "undo" button; correcting a mistake means
  creating a second adjustment, which is what keeps the ledger honest.
