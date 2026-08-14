# Phase 14D — Replenishment & Reorder Planning

> Procurement sequence: Phase 14A Purchase Orders → Phase 14B Goods Receipt / GRN → Phase 14C Purchase Reconciliation → Phase 14D Replenishment / Reorder Planning.

Replenishment is a current-state, read-only recommendation. Opening, searching, filtering,
refreshing, or exporting the page does not create a Purchase Order, Purchase, GRN, stock movement,
payable, audit entry, or product-cost change. A Purchase Order exists only after the operator opens
the normal Phase 14A entry page, reviews its editable values, and explicitly saves or submits it.

## Product configuration

Product Edit contains the optional configuration:

- **Enable replenishment planning** — defaults to `false`, including every pre-14D product.
- **Reorder Level** — reuses `Product.MinimumStock` in the product's base unit.
- **Target Stock** — reuses `Product.ReorderQuantity`; no duplicate target field was introduced.
- **Preferred Supplier** — optional `Product.PreferredSupplierId` with `SET NULL` deletion behavior.

Values must be non-negative, target must be greater than or equal to the reorder level, and
whole-unit products require whole quantities. Decimal-capable Phase 13A units preserve their
fractional precision. Disabled products are **Not configured** and are excluded from the default
recommendation list; no target is invented for existing products.

## Calculation

A configured product becomes a candidate when:

`CurrentStock <= ReorderLevel`

Current stock is always `Inventory.QuantityOnHand`. Recommendations operate in the product's base
unit and do not convert to purchase packs.

Eligible open commitment is the remaining quantity on `Submitted` or `PartiallyReceived` Purchase
Orders:

`OpenPO = max(OrderedQuantity - CompletedGRNReceivedQuantity, 0)`

Draft, Cancelled, and Completed orders are excluded. Completed GRN quantities are removed from the
commitment so a later Purchase posting cannot cause the same quantity to be counted both in live
inventory and as outstanding supply.

`ProjectedStock = CurrentStock + OpenPO`

`SuggestedAdditionalQty = max(TargetStock - CurrentStock - OpenPO, 0)`

Status is derived on every refresh: Healthy, At Reorder Level, Below Reorder Level, Out of Stock,
Not Configured, or Invalid Configuration. No status or suggested quantity is persisted.

## Estimated cost

The estimated unit cost is the newest `PurchaseItem.PurchasePriceSnapshot` from a completed
Purchase, ordered by purchase date and item identity. PO expected costs and GRN data are never used
as actual cost history. When no completed purchase exists the UI says **Cost unavailable**, rather
than assuming zero. Estimated value is planning data only and never contributes to purchase,
payable, GST, dashboard, or accounting totals.

## Creating a Purchase Order

Create PO refreshes the authoritative recommendation immediately before navigation. Selected
products, suggested base-unit quantities, and known completed-purchase costs are passed to the
existing Purchase Order entry page. When all selected products share one non-null preferred
supplier, it is preselected. Otherwise supplier selection is required in the normal PO workflow.
The operator can edit every line before saving. Existing `PurchaseOrderService` validation,
numbering, permissions, audit, and non-posting behavior remain the only PO creation path.

## Stock changes and direct purchases

Sales, purchases, returns, stock counts, and inventory adjustments are not modified. They already
change authoritative inventory, so the next replenishment refresh naturally changes or removes a
recommendation. Direct Purchases remain valid and also become the latest cost source when newer.

## Permissions and backup

Recommendation reads and navigation reuse `purchases.manage`. Product configuration writes continue
to require `products.edit`; PO saving continues to require `purchases.manage`. Service-layer checks
prevent UI bypass. The two additive Product columns are part of the ordinary SQLite database, so
the existing whole-database backup and restore path preserves them without a separate mechanism.

## Known limitations

- No automatic purchasing, supplier ranking, lead-time or demand forecasting.
- No pack-size conversion or automatic grouping into multiple supplier POs.
- Mixed/no preferred suppliers require the operator to choose a supplier in PO entry.
- Suggested-unit KPI adds base-unit quantities and should not be interpreted as a normalized weight.
- Phase 15 scope has not been specified; no Phase 15 behavior is implied here.

