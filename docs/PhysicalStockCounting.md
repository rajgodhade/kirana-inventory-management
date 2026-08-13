# Physical Stock Counting (Phase 13C)

Phase 13C lets an authorized user walk the shelves, record what is physically there, compare it
against system stock, and apply the differences as inventory adjustments — with stock changing at
exactly one moment, under explicit confirmation.

```
Start Stock Count
      ↓
Count / Scan Products      ← no stock changes here
      ↓
System Qty vs Physical Qty
      ↓
Review Variances
      ↓
Approve / Complete Count
      ↓
Inventory Adjustment Stock Movements
      ↓
Audit
```

## The central rule: counting never moves stock

While a count is `InProgress` the aggregate is pure record-keeping. No `Inventory` row and no
`StockMovement` is touched by starting a count, adding products, scanning, editing quantities,
removing items, or reviewing variances. That is what allows a count to run for an hour across a busy
shop floor without corrupting live inventory.

Inventory changes **only** in `FinalizeAsync`, and cancelling a count changes nothing at all.

## Lifecycle

| Status | Meaning |
| --- | --- |
| `InProgress` | Being counted. The only state in which anything can be added, edited or removed. |
| `Completed` | Finalized. Immutable — no item, quantity or note can change, and finalization cannot run again. |
| `Cancelled` | Abandoned without touching inventory. Kept rather than deleted so the audit trail still shows the attempt. |

There is deliberately no separate "approved" status: variance review is a UI step *before*
finalization, not a persisted state, so there is exactly one moment at which stock moves.

Immutability is enforced in one place (`EnsureEditable`) that every mutation path calls — including
finalization itself, which is what stops a count being finalized twice.

**Only one count may be open at a time**, enforced by the filtered unique index
`IX_StockCounts_SingleInProgress` rather than by a check-then-insert race in the service. Two
overlapping counts would each snapshot the same products and then apply conflicting adjustments.
Scoped/partial counts are not supported in this phase.

## Count numbers

Issued from the shared sequence infrastructure (`ISequenceGenerator`), the same mechanism behind
invoice and product codes: `STK-COUNT-000001`, `STK-COUNT-000002`, …

## Snapshot behaviour

`StockCountItem.SystemQuantity` is captured when the product is **added to the count** and never
changes afterwards. This is the figure the counter compared against, and it must not shift under them
because a sale happened while they were walking the aisle.

```
Start count:  Amul Butter, system = 120
(a sale happens)             actual = 119
Count records:               physical = 118
Observed variance:           118 − 120 = −2
```

Each item also snapshots the product's name, code, SKU, unit, and — when added by scanning — the
specific barcode that was read. Those are display/audit data for a historical record, exactly as
`SaleItem` and `PurchaseItem` snapshot theirs; `ProductId` remains the authoritative link.

## System vs Physical vs Variance

```
Variance = PhysicalQuantity − SystemQuantity
```

| System | Physical | Variance | Effect at finalization |
| --- | --- | --- | --- |
| 120 | 118 | −2 | stock decrease |
| 50 | 52 | +2 | stock increase |
| 100 | 100 | 0 | **no movement written at all** |

A zero variance writes no `StockMovement`. A ledger row saying "nothing changed" is noise that makes
real shrinkage harder to spot.

`CountedQuantity` is nullable, and that nullability is meaningful: `null` means *not counted yet*,
while `0` means *counted, and the shelf was empty*. Uncounted items are skipped entirely at
finalization — never treated as a zero count.

## Concurrency: stock moving during an open count

A count stays open while the POS keeps selling, so live stock can drift away from the snapshot. The
chosen strategy is **rebase, with the conflict surfaced** (§15 Option A):

```
Snapshot at count start : 100
Sale during the count   : 98
Physical count          : 97

Stale variance  (97 − 100) = −3  →  98 − 3 = 95   WRONG: loses 2 legitimately sold units
Rebased         (97 −  98) = −1  →  98 − 1 = 97   correct: lands on what was counted
```

The applied adjustment is always `Physical − CurrentSystemQuantity`, so the result is the counted
figure. The original snapshot is preserved untouched, so the count still reports the variance the
counter observed (−3); only the applied delta is rebased.

Rebased lines are **not silent**:

- `StockCountItem.SystemQuantityAtFinalization` records the live quantity for any line that moved.
- `StockCount.RebasedItemCount` and `StockCountResult.RebasedCount` report how many.
- The variance-review screen shows a warning InfoBar and a per-line note before anything is applied.
- The finalization audit entry appends `, N rebased onto live stock`.

> **Reading live stock correctly is subtle.** EF's identity map returns already-tracked entities, so
> a context that has had a count screen open for an hour holds *stale* `Inventory` quantities and a
> plain requery silently returns them. The service therefore reads current quantities with
> `AsNoTracking()` and copies them onto the tracked entities inside the transaction. Without this the
> rebase compares against a cached figure and the protection is defeated entirely. This was found by
> live E2E, not by unit tests — in-process tests mutate stock through the *same* context, so nothing
> ever goes stale. `FinalizeAsync_SeesStockChangedByAnotherContext_AndRebasesAgainstIt` pins it using
> a genuinely separate `DbContext`.

## Finalization is atomic

One explicit transaction covers validation, stock movements, quantity updates, the status change,
and the audit write. Any failure rolls back everything — there is no partial-finalization path.

This deliberately departs from the codebase's usual "one `SaveChangesAsync` covers everything"
pattern (following `CashRegisterService`, the existing precedent) because finalization also writes an
audit row through a separate service. A crash between the stock write and the audit write would leave
inventory moved with no record of why.

Atomicity is proven by fault injection, not assumed: `StockCountAtomicityTests` injects failures into
the stock write and into the audit write via a decorating `IKiranaDbContext` and a throwing
`IAuditLogger`, and asserts that quantities, movements, status and audit are all untouched — then
that the count can still be finalized successfully afterwards.

## Stock movement behaviour

Two new `StockMovementType` values:

- `StockCountIncrease` — surplus found by a count
- `StockCountDecrease` — shortage found by a count

They are deliberately distinct from the existing `PositiveAdjustment`/`NegativeAdjustment`: "a
stock-take found more on the shelf" and "someone corrected a number by hand" have different
credibility when investigating shrinkage, and only the former carries a count number to trace back to.

Every movement records:

```
ProductId        the counted product
MovementType     StockCountIncrease / StockCountDecrease
QuantityChange   signed, rebased against live stock
PreviousQuantity stock immediately before this movement
NewQuantity      stock immediately after
ReferenceType    "StockCount"
ReferenceId      e.g. "STK-COUNT-000001"
Reason           "Physical stock count"
UserId           who finalized
TimestampUtc     when
```

## Permissions

**No new permission key.** Stock counting is gated by the existing
`PermissionKeys.InventoryManage` ("inventory.manage"), which already means "may change stock levels"
and is already granted to Owner and Manager and withheld from Cashier. A second key for the same
authority would be free to drift apart from the first.

- **Owner** — full access
- **Manager** — full access (holds `InventoryManage` by default)
- **Cashier** — no access; the navigation entry is hidden *and* the service refuses direct calls

Enforcement is at the service layer, so a call from anywhere is refused exactly as the UI would be.
Reads (`GetActiveAsync`, `GetSummariesAsync`, `GetByIdAsync`, `GetVariancePreviewAsync`) are not
gated, matching how other read-only history screens behave.

## Unit handling

Quantities are always in the product's **stocking/base unit** (`Product.Unit`), stored at the
schema-wide `(18,3)` precision and rounded on the way in so the stored value and the variance
arithmetic agree exactly.

- Decimal quantities are accepted for units that support them: `12.5 Kg`, `8.75 Litre`.
- Fractional quantities are **rejected** for whole units — a shelf holds 3 packets, never 3.5.
- Negative physical quantities are always rejected.
- All arithmetic uses `decimal`; there is no floating-point anywhere in this feature.

**Pack conversion is deliberately not applied here.** Phase 13A's purchase-side pack model
(`10 Box → 120 Piece`) converts *purchase* quantities. A stock count counts the stocking unit
directly: 120 Piece. Scanning a "case of 12" barcode does **not** add 12 — that remains out of scope
(see Limitations).

## Barcode scanning

Scanning reuses the Phase 13B pipeline wholesale rather than re-querying barcodes:

```
Scanner → ScannerInputBuffer → IBarcodeLookupService → Product → StockCountItem
```

Consequences that come free from that reuse:

- **Any active barcode** of a product resolves to that product, and therefore to its single count item.
- Retired barcodes and barcodes on inactive products are refused, by exactly the same rule the POS uses.

A product can appear **at most once per count**, enforced by the unique index
`IX_StockCountItems_StockCountId_ProductId` as well as by the service. Scanning an
already-listed product returns the existing item rather than throwing, so a double scan is a harmless
no-op mid-aisle rather than an error to dismiss.

Scanning means *"I am counting this product"* — it adds the row and the operator types the physical
quantity. It never auto-increments a quantity, because a scan-to-increment default would silently
inflate a count whenever a barcode was read twice.

The page's Enter handler checks `IScannerInputBuffer.OnEnterPressed`'s return value before running a
manual search, the same double-add guard `PosShellPage` and `PurchaseEntryPage` use.

## Batch / expiry handling

**Stock counting is scoped to product-level stock, not batch-level.** This is a deliberate,
documented limitation.

`Inventory.QuantityOnHand` is the authoritative stock figure in this system; `ProductBatch.Quantity`
is supplementary detail (see `ProductBatch`'s own doc comment). There is no existing batch-allocation
logic that decides which batch a decrease should come from, and inventing one here — silently
draining the oldest batch, say — would produce confident-looking batch numbers that nobody verified.

So a stock count adjusts product-level stock and leaves batch quantities untouched. For a
batch-tracked product this means batch rows can disagree with total on-hand after a count. That is
visible and correctable, whereas a wrong automatic allocation would be neither.

## Audit

| Action | When |
| --- | --- |
| `StockCountStarted` | a count is opened |
| `StockCountProductCounted` | a physical quantity is recorded |
| `StockCountCancelled` | a count is abandoned |
| `StockCountCompleted` | finalization succeeds |

The completion entry carries the count number, the number of products counted, increases and
decreases with totals, the adjustment count, and the rebase count:

```
STK-COUNT-000001: 85 counted, 4 increased (+12), 7 decreased (-19), 11 adjustments, 2 rebased onto live stock
```

All entries go through the existing append-only audit logger; the inventory adjustments themselves
are additionally visible as `StockMovement` rows.

## Reports

Stock count history is part of the existing inventory reporting
(`IInventoryReportService.GetStockCountHistoryAsync`), gated by the existing `ReportsView`
permission — not a separate reporting system. It reports count number, dates, status, who counted,
products counted, increases/decreases with totals, net change, adjustment count and rebase count.

Per-count totals are derived from the `StockMovement` rows the count actually produced, not
recomputed from item snapshots. Recomputing would silently disagree with the ledger for any count
whose lines were rebased.

No sales or profit formula is modified by this phase.

## Backup / restore

`StockCounts` and `StockCountItems` are ordinary tables in the same SQLite database, so they are
included in backup and restore automatically. No separate mechanism exists or is needed.

## Migration

`20260813110000_AddStockCounts` is **purely additive**: it creates two tables and touches nothing
that already exists. It does not alter `StockMovements`, `Inventories`, or any historical row. The
two new `StockMovementType` values need no schema change because the column is a string conversion,
so existing rows keep their exact values.

Rehearsed against a copy of the real database before being applied: all 45 existing tables preserved
with identical row counts, stock-movement and inventory fingerprints unchanged, `integrity_check` ok,
`foreign_key_check` clean.

## Limitations

- **Product-level only, not batch-level** — see Batch handling above.
- **One store-wide count at a time.** No scoped counts (by category, aisle, or supplier).
- **No pack-barcode quantity conversion.** Scanning a case barcode counts one unit of the stocking
  unit, not the case contents. Doing this properly means threading a quantity through scan, cart,
  returns and movements — a phase of its own, not a column.
- **No general-purpose manual inventory adjustment screen.** The only adjustments this phase creates
  are the ones a finalized count produces. Manual adjustment workflows remain Phase 13D.
- **No mobile or Bluetooth scanning.** USB HID scanners only, as elsewhere in the app.
- **Uncounted items are skipped, not zeroed.** Adding a product to a count and never counting it
  leaves its stock alone. Counting a whole store therefore requires actually counting each product,
  not just listing them.
