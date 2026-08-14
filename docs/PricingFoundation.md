# Retail & Wholesale Pricing Foundation (Phase 15A)

Phase 15A moves selling prices out of two loose `Product` columns and into `ProductPrice` — a proper
per-level pricing table with `Retail` and `Wholesale` levels. It is a **foundation only**: the till
still charges retail, and nothing about billing changes. What changes is where a price *lives*, who
is allowed to write it, and whether a change leaves a trace.

## What stays the same

- **POS is untouched.** Billing still reads `Product.SellingPrice`, so every scan, cart line and
  receipt behaves exactly as before. Choosing a price level at the till is Phase 15B.
- **Historical transactions are untouched.** A sale keeps what it charged; `SaleItem` holds its own
  `UnitPriceSnapshot`, and no price edit reaches back into it.
- **Reports, exports and label printing** still read the same `Product` columns they always did.
- **`PermissionKeys.ProductsEdit`** governs pricing. No new permission key was introduced.
- **Wholesale was already an editable field** before this phase; it was not added here. What is new
  is that it is validated, audited, and stored as a real price level.

## The model

`ProductPrice` is the authoritative store. One row per product per level:

| Field | Purpose |
| --- | --- |
| `ProductId` | Owning product. Cascade delete. |
| `Level` | `Retail` or `Wholesale`, persisted as the enum member name (house convention). |
| `Price` | `decimal(18,2)`, the same precision as every other money column. |
| `IsActive` | False = the level was withdrawn. The row is kept, not deleted, so the price a product *used* to carry stays reconstructible. |

```
ProductPrice(Retail)     ←→  Product.SellingPrice
ProductPrice(Wholesale)  ←→  Product.WholesalePrice
        ↑                             ↑
   authoritative              synchronised projection
```

### Why the old columns remain

They are a deliberate, synchronised **projection**, not a second source of truth. POS, reports,
exports, promotions and label printing all read `Product.SellingPrice` today; retiring it in the same
phase that introduces `ProductPrice` would have meant changing the till and the pricing store at
once. Keeping it also makes the migration reversible without data loss.

The two cannot drift, because they are written by the **same method in the same `SaveChanges`** —
see `ProductPricingService.StagePrice` / `ApplyProjection`. Phase 15B retires the projection once POS
resolves prices through the service.

## Validation

- Prices are `decimal` throughout. No floating point, no custom rounding — values are rounded once,
  to 2 places, `MidpointRounding.AwayFromZero`.
- **A price may not be negative.** Enforced in `ProductPricingService.ValidatePrice` *and* in
  `ProductService.ValidateAsync`. (Before this phase, a negative *wholesale* price was silently
  accepted and persisted; that was a real bug, fixed here.)
- **Wholesale is not required to be below retail.** Some shops price unusually on purpose, and a
  rule nothing could override would be a support burden rather than a safeguard.
- **Retail is required**; it cannot be removed. Wholesale is optional.
- **At most one active price per (product, level)** — a database invariant, not a convention:
  a unique index on `(ProductId, Level)` filtered to `WHERE "IsActive" = 1`.

### NULL is not zero

This distinction is load-bearing and preserved everywhere:

| Wholesale value | Meaning | Stored as |
| --- | --- | --- |
| `NULL` | Not configured — this product has no wholesale tier | **no** active `ProductPrice` row |
| `0` | Explicitly configured as free/zero | an active `ProductPrice` row holding `0` |

In Product Edit an empty box means "not configured" (placeholder: *Not configured*); a typed `0`
means zero. Clearing the box withdraws the level rather than writing a zero.

## One write path

Every pricing mutation goes through `ProductPricingService`:

| Caller | How |
| --- | --- |
| `ProductService.CreateAsync` / `UpdateAsync` | `StagePrice`, staged into the product's own `SaveChanges` |
| `ProductImportService.CommitAsync` | `StagePrices`, staged so the whole import keeps its single-`SaveChanges` atomicity |
| Standalone price edits | `SetPriceAsync` / `RemovePriceAsync`, which add a transaction and the audit entry |

`StagePrice` is the shared, non-saving core: it validates, rounds, creates/updates/deactivates the
row, writes the projection, and reports what changed. Callers keep their own transaction boundaries
but share one implementation of the arithmetic — which is what makes drift impossible rather than
merely unlikely.

`ProductBatch.SellingPrice` and `SaleItem.UnitPriceSnapshot` are **not** part of this: they are
per-batch and per-transaction snapshots, deliberately independent of current pricing.

## Import behaviour

Import is **additive** about wholesale. An import file is rarely the complete truth about a product,
so a missing value leaves the existing tier alone rather than withdrawing it — otherwise an ordinary
retail price-list import would silently strip every product's wholesale price.

| Case | Behaviour |
| --- | --- |
| `Wholesale Price` column present with a value | Value is applied |
| `Wholesale Price` column absent | Existing wholesale is **preserved** |
| `Wholesale Price` column present but **blank** | Existing wholesale is **preserved** |
| `Wholesale Price` = `0` | Stored as a configured zero |
| Retail (`Selling Price`) | Required; always applied |

The blank case deserves a note: `ParseOptionalDecimal` maps both an absent column and a blank cell to
`null`, so the importer genuinely **cannot distinguish** the two. Preserve-on-blank is therefore the
actual behaviour, and **import cannot clear a wholesale price** — that is a Product Edit operation.
Pinned by tests so any future change to this has to be deliberate.

## Audit

Each level that actually moves produces exactly one entry:

| Action | When | Carries |
| --- | --- | --- |
| `PriceChanged` | A level's price changed | previous, new, level (in `Reason`), user, timestamp |
| `PriceRemoved` | A level was withdrawn | previous value, level, user |
| `PriceModification` | **Purchase price or MRP** changed | unchanged from its pre-15A meaning |

Re-saving the same number writes nothing — a no-op is not an event, and logging it would bury real
price changes in noise. `PriceModification` was deliberately narrowed to cost/MRP so that selling
price changes are no longer lumped in with them.

For standalone edits the price and its audit entry share a transaction, so a price can never change
without a record of who changed it.

## Permissions

Both layers are enforced, and both must stay:

```
caller → ProductService      → ProductsEdit
       → ProductPricingService → ProductsEdit  → ProductPrice
```

`ProductPricingService` checks on its own entry points (`SetPriceAsync`, `RemovePriceAsync`) rather
than relying on `ProductService` having checked first — otherwise any future caller that talks to the
pricing service directly would be a silent authorization bypass.

## Migration

`20260814141847_Phase15APricingFoundation` — purely additive. It creates `ProductPrices`, its
foreign key and both indexes, then backfills:

- every product gets exactly one active `Retail` row from `SellingPrice`;
- products with a non-NULL `WholesalePrice` get a `Wholesale` row;
- `NULL` wholesale produces **no** row.

The backfill copies **column to column** — no `ROUND`, no `CAST`, no expression. Money is stored as
TEXT in this SQLite schema, so any computation could alter the stored representation of a value like
`57.50`; a direct copy cannot.

The unique index is created **before** the backfill, the opposite of the Phase 13B barcode migration.
There, legacy case-only duplicates genuinely existed and had to be resolved by the INSERT itself.
Here each product can yield at most one row per level by construction, so putting the index first
turns any unexpected duplicate into a loud failed migration rather than silently bad pricing data.

`Product.SellingPrice` and `Product.WholesalePrice` are **not** dropped or altered.

### Verifying a migration

Prices are described either side of the migration and the descriptions compared:

```
before:  ProductId + SellingPrice + WholesalePrice      (legacy columns)
after:   ProductId + Retail + Wholesale                 (ProductPrices)
```

The two fingerprints must be identical. This single check catches a changed price, a skipped
product, a duplicate, `NULL` becoming zero, and any change in decimal representation. It is
complemented by a per-table content hash proving no other table changed.

> When verifying by hand, compare money with `CAST(x AS REAL)`. These columns have TEXT affinity, so
> a bare `WholesalePrice = 0` silently matches nothing.

## Testing

| Area | Where |
| --- | --- |
| Pricing behaviour, audit, permissions, atomicity, historical protection | `ProductPricingIntegrationTests` |
| The contract Product Edit depends on (full-request saves, null/zero, negatives) | `ProductEditPricingContractTests` |
| Import additive-wholesale semantics | `ProductImportServiceTests` |
| Table, indexes, FK, backfill, price fingerprint | `MigrationSchemaTests` |

Migration tests apply the **real migration chain** to a throwaway database (`IMigrator`, migrate to
the previous migration, insert legacy rows, migrate forward). The shared fixtures use
`EnsureCreated()`, which builds from the model and never executes migration SQL — so the backfill is
only ever exercised there.

Every protection was verified by fault injection: each was disabled in turn, the specific tests that
caught it were recorded, and the protection restored. Negative validation, duplicate-active-price,
authorization (both layers), audit, retail projection drift, wholesale projection drift, and
historical-snapshot mutation were all caught by the tests that should catch them.

## Limitations

Phase 15A deliberately does **not** include:

- customer-specific or customer-group pricing
- any price level beyond Retail and Wholesale
- quantity, slab or bulk pricing
- promotions, coupons or loyalty pricing
- margin enforcement or minimum-price rules
- scheduled or time-bound pricing
- bulk price management / mass repricing tools
- **POS price-level selection** — the till always charges retail

Clearing a wholesale price is possible only through Product Edit, not through import.

## Next: Phase 15B — POS price resolution

Phase 15B is where a stored wholesale price starts to matter at the till. Candidates for that phase:

- selecting a price level on a bill
- customer-linked default price levels
- quantity-based resolution
- retiring `Product.SellingPrice` once POS reads through the pricing service

None of that exists yet. Until then, a wholesale price is recorded, validated and audited — but not
charged.
