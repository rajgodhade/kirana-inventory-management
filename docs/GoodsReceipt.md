# Phase 14B — Goods Receipt / GRN

> Procurement sequence: Phase 14A Purchase Orders → Phase 14B Goods Receipt / GRN → Phase 14C read-only Purchase Reconciliation → Phase 14D Replenishment / Reorder Planning. See [ReplenishmentAndReorderPlanning.md](ReplenishmentAndReorderPlanning.md).

Goods Receipts record the quantity that physically arrived against a submitted Purchase Order.
They are deliberately separate from both the commercial intent (the Purchase Order) and the
accounting/inventory transaction (the Purchase).

## Procurement flow

```text
Purchase Order (intent, non-posting)
    -> Goods Receipt (physical arrival, non-posting)
        -> Purchase (invoice, inventory and supplier payable posting)
```

The original direct Purchase workflow remains supported for goods that are ordered, received,
and invoiced at the same time. A Purchase Order or GRN is not required for a direct Purchase.

Phase ownership is:

- 14A: Purchase Orders
- 14B: Goods Receipt / GRN
- 14C: Purchase Reconciliation
- 14D: Replenishment / Reorder Planning

## Lifecycle

- `Draft`: created from an eligible PO and may be cancelled or completed.
- `Completed`: immutable receipt history. It may be used once to prefill and finalize a Purchase.
- `Cancelled`: retained as history with the cancelling user, time, and reason.

Only `Submitted` and `PartiallyReceived` Purchase Orders can be received. Draft, Cancelled, and
Completed orders are rejected by the service even if a caller bypasses the UI.

## Partial receiving and quantity rules

Each completed GRN contributes to the received quantity of its Purchase Order lines. Receiving
less than the remaining order changes the PO to `PartiallyReceived`; receiving the final remaining
quantity changes it to `Completed`. Multiple GRNs, and therefore multiple actual Purchases, may
belong to one PO.

The service rejects negative quantities, an all-zero receipt, duplicate PO lines, and quantities
above the current authoritative remainder. Fractional quantities are accepted only for units that
support them; `Piece` remains whole-number only. No pack conversion is performed.

## Inventory and supplier payable boundary

Creating, completing, viewing, or cancelling a GRN does not update inventory, StockMovement,
product cost, supplier balance, or SupplierPayment. `PurchaseService.FinalizePurchaseAsync`
remains the single authoritative posting path. Finalizing a Purchase created from a GRN posts the
received quantity exactly once and applies the existing purchase pricing, GST, payment, inventory,
and payable rules.

The Purchase screen is prefilled with the supplier, products, and received quantities. Quantities
and source identity are locked, while supplier invoice number, actual price, discount, tax,
invoice date, and payment remain part of the existing Purchase workflow. PO expected cost remains
a historical estimate and is never overwritten by actual invoice cost.

Each source GRN can create at most one Purchase. That Purchase stores both `GoodsReceiptId` and
`PurchaseOrderId`, providing the trace `PO -> GRN -> Purchase` without duplicating Purchase data.

## Historical snapshots and barcode behavior

GRNs snapshot supplier name/code and product name/code/SKU/unit/ordered quantity so history stays
readable after master-data changes. Product identification accepts the product's primary barcode
or any active alternate barcode through the Phase 13B normalization rules. Retired barcodes are
rejected, and a barcode never changes quantity or performs a pack conversion.

## Concurrency and atomicity

UI remainder values are advisory. Completion opens a database transaction, re-reads the GRN, PO,
PO lines, and all prior completed receipt quantities, and rejects an over-receipt based on those
fresh values. GRN completion, PO status change, and completion audit are committed together; an
audit or persistence failure rolls the transaction back.

Purchase finalization continues through the established PurchaseService SaveChanges boundary so
the Purchase, PurchaseItems, inventory, StockMovement, supplier payable, and source link are one
posting operation. The GRN service never creates a parallel stock movement.

## Authorization and audit

All GRN reads and mutations enforce the existing `purchases.manage` permission at the service
layer. UI visibility is only a convenience and is not the security boundary.

The existing audit system records creation, completion, cancellation, and Purchase creation from
a GRN with document references, supplier, quantities, user, and timestamp.

## Backup, restore, and maintenance

GRNs and their links are ordinary EF/SQLite tables and are included in physical database backups.
Restore summaries and database-maintenance row counts include Purchase Orders and Goods Receipts.
Orphan diagnostics cover GRN-to-PO, GRN-item-to-GRN, and GRN-item-to-PO-item relationships.

## Known limitations

- No receiving tolerance or approval override; over-receiving is always rejected.
- No reconciliation, supplier price history, automatic procurement, or reorder planning.
- Completed GRNs are not reversed automatically. Corrections use existing Purchase Return or
  Inventory Adjustment workflows after a Purchase has posted.
- One GRN maps to at most one Purchase; separate supplier invoices require separate GRNs.
