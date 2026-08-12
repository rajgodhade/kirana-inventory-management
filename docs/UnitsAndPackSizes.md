# Units, Pack Sizes & Unit Conversion (Phase 13A)

Phase 13A adds an optional, validated purchase pack on top of the existing single-unit product
model, so a shop can buy in bulk (e.g. 10 Box, 1 Box = 12 Piece) without changing how stock,
selling, billing, or reporting work. It does not add multi-unit selling — that stays out of scope.

## What stays the same

- `Product.Unit` remains the single stocking/selling unit for a product, exactly as before. Every
  existing product, sale, return, and report keeps working unchanged.
- Pricing is unchanged: `PurchasePrice`/`Mrp`/`SellingPrice` are always **price per base unit**
  (`Product.Unit`), never per pack. A pack purchase only converts *quantity* — the price box always
  means the same thing it always has.
- POS billing never shows a unit picker. A product is always sold and rung up in its one
  `Product.Unit` — the only thing Phase 13A changes there is a cosmetic label (e.g. "Kilogram"
  reads as "Kg", "Packet" reads as "Pack") via `UnitOfMeasureExtensions.ToDisplayText()`, or a
  product's own `UnitDisplayText` override if one is set (e.g. "500g Pack").

## The optional purchase pack

Two new nullable fields on `Product`:

- `PurchasePackUnit` (`UnitOfMeasure?`) — the bulk unit a product can be *purchased* in (e.g. Box).
- `PurchasePackSize` (`decimal?`) — how many `Unit` one `PurchasePackUnit` equals (e.g. 12).

Both are null unless explicitly configured — the overwhelming majority of products need neither.
They only ever affect Purchase Entry.

When a purchase line is entered in pack mode (e.g. "10 Box"), the app converts it to the base-unit
quantity (`10 × 12 = 120 Piece`) and submits that as the line's `Quantity` — the single field every
downstream system (`PurchaseService`, `Inventory.QuantityOnHand`, `StockMovement`,
`ProductBatch.Quantity`, `PurchaseReturnService`) has always understood. None of that code changed
in Phase 13A; it only ever sees a (possibly larger) base-unit quantity, exactly like before.

`PurchaseService.FinalizePurchaseAsync` also receives the raw pack unit/quantity as supplementary
metadata (`PurchaseLineInput.PurchasedPackUnit`/`PurchasedPackQuantity`) and **validates** that they
agree with the submitted base-unit `Quantity` — it does not derive `Quantity` from them. A
disagreement (tampered or buggy payload) is rejected outright rather than trusted. The agreed
values are snapshotted onto `PurchaseItem.PurchasedPackUnitSnapshot`/`PurchasedPackQuantitySnapshot`
purely for later display/audit (e.g. "10 Box (120 Piece)" on a purchase record) — they are never
read back into any calculation.

## Internal quantity representation

`Inventory.QuantityOnHand` and every `StockMovement` row are always denominated in `Product.Unit`,
before and after Phase 13A. A pack purchase never leaves a partially-converted number anywhere —
the conversion happens once, before the line reaches any inventory-mutating code.

## Conversion rules

`Kirana.Domain.Entities.UnitConversion` is the one place conversion arithmetic happens:

- `ToBaseQuantity(packQuantity, packSize, packUnit, baseUnit)` — throws if `packSize <= 0`,
  `packUnit == baseUnit` (no self-conversion), or `packQuantity <= 0`; otherwise returns
  `packQuantity * packSize` using `decimal` throughout (no floating-point involved anywhere).
- `IsValidPackConfiguration(packSize, packUnit, baseUnit)` — the non-throwing predicate used by
  validation: both null is valid (no pack configured); exactly one set is invalid; both set
  requires a positive size and a pack unit different from the base unit.

The same engine correctly handles any unit pair with a known ratio (e.g. 1 Kg = 1000 Gram, 1 Litre
= 1000 Millilitre, 1 Carton = 24 Box → 288 Piece when chained) — it is not limited to Piece-family
units, even though the shipped UI mainly targets bulk-pack purchasing (Box/Carton/Packet/Dozen).

## Import

`ProductImportService` gained three optional columns: `Purchase Pack Unit`, `Purchase Pack Size`,
`Unit Display Text`. A file without these columns imports exactly as before (both pack fields
resolve to null). An unrecognized pack-unit token, a non-positive pack size, or a pack unit equal
to the product's own unit produces a row-level validation error — the row is excluded from commit
while the rest of the file still imports.

## Migration

`20260813090000_AddUnitPackFields` adds five nullable columns with no default values and no
backfill: `Products.PurchasePackUnit`, `Products.PurchasePackSize`, `Products.UnitDisplayText`,
`PurchaseItems.PurchasedPackUnitSnapshot`, `PurchaseItems.PurchasedPackQuantitySnapshot`. Existing
rows simply get NULL, which is the same as "no pack configured" — there is no way for this
migration to alter an existing product's unit, an existing inventory quantity, or any historical
sale/purchase/return. Verified against both a fresh database and a copy of a production database
before being considered safe.

## Limitations / deferred work

- **Multiple barcodes per product (Phase 13B)** are intentionally not implemented here. Phase 13A
  only establishes the unit/pack foundation; adding a second identifier scheme is a distinct
  concern that would otherwise inflate this phase's risk and blast radius.
- **Physical stock counting (13C)** and **inventory adjustment workflows (13D)** are also out of
  scope — Phase 13A does not add any new way to directly edit `QuantityOnHand`; every quantity
  change still goes through a real sale, purchase, or return and writes a `StockMovement` row.
- Selling in a pack unit at POS (e.g. ringing up "2 Box" directly) is not supported — only
  purchasing is. Adding that would require a unit picker in the cart and touch
  `SaleItem`/`SalesReturnService`/reports much more deeply; the base-unit-only design in this phase
  was a deliberate scope decision to keep the blast radius small.
