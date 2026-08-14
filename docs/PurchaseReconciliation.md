# Phase 14C — Purchase Reconciliation

## Purpose and document ownership

Purchase reconciliation is a read-only control view over the procurement chain:

`Purchase Order (plan) → Goods Receipt / GRN (physical receipt) → Purchase (financial and inventory posting)`

Each document remains authoritative for its own purpose. Reconciliation does not post stock, create a payable, create GST, change a document status, or write an audit entry. There is no manual **Mark Reconciled** state.

## Architecture

`PurchaseReconciliationService` queries existing `PurchaseOrder`, `GoodsReceipt`, and `Purchase` records with no tracking. It has no audit logger and never calls `SaveChanges`. Reopening or refreshing the page always derives a new result from current authoritative records and shows the calculation time.

No reconciliation table or database migration is required. Existing backup and restore behavior therefore already preserves every source needed to reproduce the report.

Only non-draft, non-cancelled purchase orders participate. Only completed GRNs contribute received quantities and only completed purchases contribute invoiced quantities and actual financial values. Direct purchases with no `PurchaseOrderId` remain valid and are excluded from PO reconciliation.

The service performs three bounded, set-based reads—purchase orders, their GRNs, and their linked purchases—rather than one query per row. Product aggregation uses `ProductId` and `PurchaseOrderItemId`; names, codes, SKUs and barcodes are display snapshots only.

## Quantity formulas

For each purchase-order line:

- Ordered = PO ordered quantity.
- Received = sum of completed GRN item quantities linked to that PO item.
- Purchased = sum of completed, PO-linked purchase-item quantities for the product.
- Pending receipt = `max(Ordered − Received, 0)`.
- Pending purchase/invoice = `max(Received − Purchased, 0)`.
- Over-received = `max(Received − Ordered, 0)`.
- Over-invoiced = `max(Purchased − Received, 0)`.

Multiple completed GRNs and multiple completed purchases are summed. Values stay in the unit already stored by Phase 13A; reconciliation performs no pack conversion and does not reinterpret decimal quantities.

## Cost, discount and GST formulas

- Expected unit cost = immutable PO item unit cost snapshot.
- Actual unit cost = quantity-weighted purchase price: `sum(Quantity × PurchasePriceSnapshot) / sum(Quantity)`.
- Unit cost variance = `ActualUnitCost − ExpectedUnitCost`.
- Unit variance percent = `UnitCostVariance / ExpectedUnitCost × 100` when expected cost is non-zero.
- Expected total = stored PO line total.
- Actual line total = sum of stored purchase-item line totals.
- Total variance = `ActualTotal − ExpectedTotal`.
- Expected/actual discounts and GST use the amounts stored on the corresponding historical line items.
- Displayed tax variance is `ActualTax − ExpectedTax`. For mismatch detection on a partial purchase, expected PO tax is proportionally compared to the invoiced quantity so an unreceived quantity is not incorrectly labelled a GST-calculation error.

The reconciliation layer does not recalculate GST and does not introduce a second tax engine.

## Derived flags

A PO can have several flags simultaneously:

- Fully reconciled
- Awaiting receipt / partially received
- Awaiting purchase / pending purchase
- Quantity mismatch
- Price mismatch
- Tax mismatch
- Over-received
- Over-invoiced
- Exception

Fully reconciled is derived only when ordered, received and purchased quantities agree and there is no price, tax, over-receipt or over-invoice exception.

## UI and navigation

The **Purchase Reconciliation** management page provides KPI cards, expected/actual/variance totals, supplier/date/status filters, document-number search, refresh, CSV/Excel/PDF export, and a horizontally scrollable line comparison. The detail page links back to the PO and its GRN/Purchase source documents.

Navigation is available from Purchase Orders, Goods Receipts, and PO-linked Purchase details. A direct Purchase has no reconciliation action because it has no PO/GRN chain.

## Permissions and audit

The existing `purchases.manage` permission gates both service reads and navigation. Reconciliation offers no mutations. Opening, searching, exporting, or refreshing it does not create procurement audit noise; the existing audit histories of PO, GRN and Purchase remain authoritative.

## Known limitations

- A purchase line does not currently carry `PurchaseOrderItemId`; actual purchase quantities are matched by stable `ProductId`. The purchase-order workflow prevents duplicate product lines, so the mapping is unambiguous in supported data.
- Expected-versus-actual tax is limited to amounts stored by the Phase 14A/14B models; no invented historical tax detail is generated.
- Reconciliation is calculated on demand and is not a supplier-statement or accounting-ledger reconciliation.
- Phase 14D adds read-only replenishment and reorder planning. It remains architecturally separate from reconciliation.

## Procurement roadmap

- Phase 14A: Purchase Orders — planned supplier commitments.
- Phase 14B: Goods Receipt / GRN — physical delivery records.
- Phase 14C: Purchase Reconciliation — read-only PO/GRN/Purchase comparison.
- Phase 14D: Replenishment / Reorder Planning — current-state recommendations and reviewed PO handoff.
