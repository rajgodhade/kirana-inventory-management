# Price Resolution (Phase 15B-1)

Phase 15A gave prices a home: `ProductPrice`, with a `Retail` and an optional `Wholesale` level.
Phase 15B-1 gives them a **read path** — one testable service that answers *"what does this product
cost under these circumstances?"* — before anything at the till starts asking that question.

## Why this exists

Billing today does, in effect:

```
Product → Product.SellingPrice → sale price
```

That is correct for retail and nothing more. Every pricing feature after this point — selecting a
level on a bill, customer agreements, quantity breaks — needs the shape:

```
Product + PricingContext → resolved price
```

Adding those rules directly into `SaleService` would scatter pricing decisions through checkout,
where they are hard to test and easy to get subtly wrong. So the abstraction is built and proved
first, and wired into POS later.

## POS is not integrated yet

**This phase changes no billing behaviour.** `SaleService` still reads `Product.SellingPrice`, the
cart still prices the same way, and price override, discounts and tax are untouched. The resolver is
registered in DI and covered by tests, but nothing calls it in production yet. Integration is a
later 15B task.

## ProductPrice is the read source

The resolver queries `ProductPrice`, **never** the projection columns:

```
PricingContext
     ↓
IProductPriceResolver
     ↓
ProductPrice          ← authoritative
     ↓
PriceResolution
```

What it deliberately does **not** do:

```csharp
// WRONG — this is two pricing systems again
if (level == Retail)    return product.SellingPrice;
if (level == Wholesale) return product.WholesalePrice;
```

`Product.SellingPrice` and `Product.WholesalePrice` remain compatibility projections for POS,
reports, exports and label printing (see [PricingFoundation.md](PricingFoundation.md)). Branching
per level across them would rebuild exactly the split Phase 15A removed.

Two tests exist purely to prove this. They desynchronise a projection column on purpose — the one
state the pricing service exists to prevent — and assert the resolver still returns the
`ProductPrice` value:

| ProductPrice | Projection column | Resolver returns |
| --- | --- | --- |
| Retail 100 | `SellingPrice` = 95 | **100** |
| Wholesale 90 | `WholesalePrice` = 95 | **90** |

A resolver that read the projections would pass every other test in the suite and fail only these.

## PricingContext

```csharp
public sealed record PricingContext(PriceLevel PriceLevel);
```

Deliberately minimal — today the only input is the level. It is a *type* rather than a bare
parameter so later phases can add a customer, a quantity or a date without changing the resolver's
signature or every call site. Nothing is added speculatively: a field nobody reads is a field that
will eventually be mis-set.

`PricingContext.Retail` and `PricingContext.Wholesale` are provided for the common cases.

## PriceResolution

The result is an outcome, not a nullable decimal:

```csharp
resolution.IsResolved      // false ⇒ there is no applicable price
resolution.UnitPrice       // decimal?, non-null exactly when IsResolved
resolution.Level           // the level that was ASKED for, echoed back
resolution.Source          // ConfiguredPrice (the only origin so far)
resolution.UnavailableReason
```

"No wholesale price" is a legitimate answer — not an error, and emphatically not zero. A caller
cannot tell that apart from a genuine price of `0` using a null alone, which is why the outcome is
typed. `IsResolved` is annotated with `MemberNotNullWhen`, so the compiler enforces checking it
before reading `UnitPrice`.

`PriceSource` currently has one member, `ConfiguredPrice`. Promotions, customer agreements and
quantity breaks will add members here rather than a second result type.

## Behaviour

| Situation | Result |
| --- | --- |
| Retail configured | resolved, the stored decimal exactly |
| Wholesale configured | resolved, the stored decimal exactly |
| Wholesale = `0` (explicitly configured) | **resolved at 0** — a configured zero is a price |
| Wholesale not configured | unavailable, `LevelNotConfigured` |
| Level withdrawn (`IsActive = false`) | unavailable, `LevelNotConfigured` |
| Product inactive | unavailable, `ProductInactive` |
| Product id unknown | `InvalidOperationException` |
| Two active rows for one level | `InvalidOperationException` |

### No fallback policy

Asking for wholesale on a product that has none returns **unavailable**. It never silently becomes
the retail price.

Whether wholesale *should* ever fall back to retail is a policy decision with real money attached —
it belongs in a phase where someone decides it deliberately, not buried inside a lookup. 15B-1 does
not invent it.

### Inactive products

An inactive product resolves to `ProductInactive` rather than throwing. This follows the existing
POS **read** convention: `BarcodeLookupService` filters `b.Product.IsActive` so a discontinued
product simply does not come back. `SaleService` keeps its own "inactive and cannot be sold" throw
for anything that reaches a bill — the resolver does not duplicate that rule, it only reports that a
discontinued product has no current price.

### Unknown product

Throws `InvalidOperationException`, matching `SaleService` (`"Product #N was not found."`) and
`ProductPricingService`. An unknown id is a caller bug, not a pricing outcome. It never returns zero.

### Duplicate active prices

Phase 15A's filtered unique index on `(ProductId, Level) WHERE IsActive = 1` makes this unreachable
through the application. If corrupt data is encountered anyway, the resolver **throws rather than
choosing** — picking one arbitrarily would mean billing a customer a price selected by row order.
Covered by a test that drops the index to manufacture the state; no new constraint was added.

## Read-only

Resolving a price writes nothing: no entity change, no audit row, no stock movement, no sale. The
change tracker is left clean, so calling it repeatedly is free and a cart can re-price itself as
often as it likes.

Reading a price also requires **no permission**. It is what a cashier does on every scanned line,
and neither `BarcodeLookupService` nor `ProductPricingService.GetPriceAsync` gates a read. The
mutation permission (`ProductsEdit`) is unchanged and still governs price *changes*; a test asserts
that a cashier refused a price change can still resolve prices. `PricingChangeSellingPrice` and the
existing POS override behaviour are untouched.

## Query behaviour

One query, one round trip, per resolve:

```sql
SELECT p.IsActive,
       (prices at the requested level where IsActive = 1)
FROM Products p
WHERE p.Id = @productId
```

- `AsNoTracking()`, projected to an anonymous type — so the resolver can never hand back a stale
  tracked entity. This matters: an EF identity-map stale read caused a real bug in Phase 13C, and a
  test here proves a price committed by a *different* context is what the resolver returns.
- No `Include`, no `Product` graph loaded.
- No N+1: resolving N products currently costs N queries by design (one call per product). A batch
  API can be added when POS integration shows it is needed.
- **No caching.** Deliberately. The first implementation is deterministic and database-backed;
  caching can be evaluated once the till actually depends on this.

## Testing

`ProductPriceResolverTests` covers retail, wholesale, exact decimals, zero-vs-unconfigured,
withdrawn levels, inactive products, unknown ids, cross-product and cross-level isolation, fresh
cross-context reads, duplicate-row refusal, and read-side-effect freedom.

Every protection was verified by fault injection — the resolver was rewritten to read
`Product.SellingPrice`, to read `Product.WholesalePrice`, to fall back wholesale→retail, to ignore
`IsActive`, to serve a cached tracked price, to return zero instead of failing, and to write during a
read. Each was caught by the test that should catch it, and each was restored.

## Limitations

Phase 15B-1 provides resolution only. It does **not** include:

- POS integration — billing still reads `Product.SellingPrice`
- any POS price-level selector
- automatic wholesale selection
- customer-specific or customer-group pricing
- quantity, slab or bulk pricing
- promotions, coupons, loyalty or combo pricing
- scheduled or time-bound pricing
- margin enforcement or below-cost blocking
- changes to price override behaviour
- caching or batch resolution

## Next

**Phase 15B-2** should integrate the resolver into POS: have the cart obtain its unit price through
`IProductPriceResolver` with `PricingContext.Retail` instead of reading `Product.SellingPrice`
directly, keeping billing behaviour identical. That swap is what makes every later pricing feature a
change of *context* rather than a change to checkout — and it is the step that finally lets the
projection columns retire.
