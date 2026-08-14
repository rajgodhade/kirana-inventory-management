# Phase 14A — Purchase Orders

> Procurement sequence: Phase 14A Purchase Orders → Phase 14B Goods Receipt / GRN → Phase 14C read-only Purchase Reconciliation → Phase 14D Replenishment / Reorder Planning. See [ReplenishmentAndReorderPlanning.md](ReplenishmentAndReorderPlanning.md).

Purchase Orders are non-posting procurement documents. They reuse the existing supplier and
product masters, product search (including active alternate barcodes), purchase GST calculator,
sequence generator, audit log, and `purchases.manage` permission.

## Lifecycle

- `Draft` can be edited, submitted, or cancelled.
- `Submitted` is immutable and can be received or cancelled.
- `PartiallyReceived` is set by Phase 14B after one or more completed GRNs have received less than
  the ordered quantity. It remains eligible for another receipt.
- `Completed` is set by Phase 14B only when completed GRNs account for every ordered quantity.
- `Cancelled` is retained with timestamp, user, and reason.

## Posting boundary

Creating, editing, submitting, viewing, printing, or cancelling a Purchase Order never writes to:

- `Inventory` or `StockMovement`
- `Purchase` or `PurchaseItem`
- `SupplierPayment` or `Supplier.OutstandingBalance`
- product purchase cost

## Authorization

Phase 14A deliberately reuses `purchases.manage`; no duplicate permission was added. Owner and
Manager receive it through the existing role defaults. The service enforces the permission on
every read and mutation, and the management navigation hides the module without it.

## Procurement architecture

Phase 14B adds the non-posting `Purchase Order -> Goods Receipt` relationship and then reuses the
existing Purchase workflow for the authoritative inventory and supplier-payable posting. See
[GoodsReceipt.md](GoodsReceipt.md) for partial receipt, concurrency, audit, and Purchase handoff
rules. Phase 14C adds Purchase Reconciliation and Phase 14D adds recommendation-only Replenishment / Reorder
Planning.
