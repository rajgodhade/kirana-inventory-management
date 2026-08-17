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

## POS integration (Phase 15B-2)

The till now prices through the resolver. Billing behaviour is unchanged — the same numbers on the
same bills — but the *source* of those numbers moved:

```
before:  Product → Product.SellingPrice → cart → SaleItem.UnitPriceSnapshot
after:   Product + PricingContext.Retail → resolver → ProductPrice → cart → SaleItem.UnitPriceSnapshot
```

**POS always uses `PricingContext.Retail`.** There is no price-level selector, no wholesale button,
no customer pricing and no automatic wholesale selection. Wholesale can be stored and edited, but it
has no effect on a bill.

### Resolved twice, on purpose

Both the cart and `SaleService` resolve — and that is deliberate, not duplication:

- **`PosShellViewModel`** resolves to *display* the price when a line is added or a held bill is
  resumed, so the operator sees what will be charged.
- **`SaleService.CompleteSaleAsync`** resolves again because it is the **trust boundary**. A client
  can put any number in `UnitPriceOverride`, and the override check decides whether that number
  needed manager authorization by comparing it against the real price. If that comparison used a
  client-supplied figure it would be meaningless, so the server re-establishes the price from the
  authoritative store regardless of what the UI displayed.

Both ask the same resolver with the same context, so they agree. The server's answer is the one that
gets billed.

### Failure policy

If retail cannot be resolved, the sale is **refused**. No fallback to wholesale, to
`Product.SellingPrice`, to MRP, to purchase cost, or to zero — a product that cannot be priced is a
product that must not be sold.

Resolution happens **before any write**, so a refused sale leaves no sale, no sale item, no stock
movement and no audit row. The cart refuses at scan time too, so the operator hears about it while
they can still act on it rather than at payment.

### Price override is unchanged

`PricingChangeSellingPrice` and the existing override flow are untouched. One subtlety worth
stating: an override *equal to the resolved retail price* still counts as "no override" and needs no
authorization. Because the comparison now uses the resolved price rather than the projection column,
a diverged projection can no longer cause ordinary sales to start demanding manager PINs.

### Historical snapshots

`SaleItem.UnitPriceSnapshot` records the resolved price actually charged and is never rewritten. A
later retail change moves tomorrow's sales, not yesterday's, and returns continue to work off the
original sale's figures.

## POS price-level selection (Phase 15B-3)

A bill can now be sold at **Retail** or **Wholesale**. The till shows a compact two-toggle selector
next to the search box; everything else about billing is unchanged.

### Default is Retail, always

A new bill, a new tab, and the bill that follows a completed sale all start at Retail. Wholesale is
a deliberate per-bill choice and never persists past the bill it was chosen for — a cashier who
serves a wholesale customer cannot accidentally discount the next walk-in.

### Where the level lives

On the active bill. `PosShellViewModel.SelectedPriceLevel` is the live value, snapshotted into
`BillSessionViewModel.PriceLevel` when tabs switch — the same place and mechanism the customer, bill
discount and manager authorizations already use. One authoritative copy; the per-tab entries are
storage, not a second source of truth.

Switching tabs **restores** a tab's level without re-pricing it. A bill's lines already hold the
prices they were resolved at, and re-pricing on tab switch would change amounts a cashier had
already quoted just because they glanced at another bill.

### Switching re-prices the cart

Choosing a level re-resolves **every** line through `IProductPriceResolver` at that level. Nothing
reads `Product.WholesalePrice` directly.

Switching also **discards a manual override** on the affected line: the override was a deviation
from a price that no longer applies, so carrying the number across would silently turn an approved
retail price into an unapproved wholesale one.

### A product with no price at the chosen level

The line is **flagged**, not silently repriced and not left quietly at the old level. It keeps
showing its previous amount so the change is visible, the error names the product
("Wholesale price is not configured for Amul Butter 500g."), and **payment is blocked** until every
flag clears — by switching back to a level the product has, or removing the line.

A mixed cart therefore refuses as a whole rather than billing some lines at wholesale and one at
retail. An invoice headed "Wholesale" must not contain a line that isn't.

### Products added after switching

A product scanned onto a wholesale bill joins it at wholesale. It does not arrive at retail and wait
to be re-priced.

### The server still decides

`CompleteSaleRequest` carries the **level**, never the amounts. `SaleService` re-resolves every line
from that level at the trust boundary, exactly as it did for retail in 15B-2 — so choosing
"Wholesale" cannot become a way for a client to name its own price. A product that cannot be priced
at the requested level fails the sale, with nothing written.

### Override is still a separate mechanism

`PricingChangeSellingPrice` and the existing override flow are untouched. The baseline the override
is measured against is the **resolved price at the bill's level**, so on a wholesale bill an
override up to the retail price is still an override and still needs authorization. Selecting a
cheaper level does not move the bar for what a manager must approve.

### History

`SaleItem.UnitPriceSnapshot` holds the resolved level price actually charged and is never rewritten.
A later wholesale price change moves tomorrow's wholesale bills, not yesterday's, and returns
continue to work off the original sale's figures.

### Not persisted: which level a sale used

`Sale`/`SaleItem` deliberately gained **no new column**. `UnitPriceSnapshot` records what was
charged, no existing report asks "was this sold at wholesale?", and no requirement for that has been
stated — so no migration was created.

This is a real limitation worth knowing: after a price change you cannot reliably tell a historical
wholesale sale from a retail one by comparing snapshots.

> **Backlog:** consider `Sale.PriceLevel` persistence if historical Retail vs Wholesale reporting
> becomes a requirement. The smallest change is a single `PriceLevel` column on **`Sale`** (the level
> is bill-wide, not per-line), defaulted and backfilled to `Retail` for existing rows. Deliberately
> not implemented now — it is a schema change that should be driven by a stated reporting need
> rather than added speculatively.

### The payment guard exists at both layers

The UI blocks payment while any line is unresolved, naming the product early. Independently,
`SaleService` re-resolves **every** line at the submitted level and refuses the request if any one
fails — so a client that skipped the UI could not submit an unresolved bill either. The UI guard is
for the cashier; the service guard is the one that actually protects the money.

## Customer default price level (Phase 15B-4)

A customer can carry an optional `DefaultPriceLevel`, which decides what level their bills **open**
at. It is a default, not a lock, and never a pricing authority.

### NULL means "no preference"

| `Customer.DefaultPriceLevel` | Bill opens at | Meaning |
| --- | --- | --- |
| `NULL` | Retail | Nobody has classified this customer |
| `Retail` | Retail | Someone decided this is a retail customer |
| `Wholesale` | Wholesale | Someone decided this is a wholesale customer |

`NULL` and an explicit `Retail` produce the same opening level but are stored differently, because
they are different facts. The migration therefore **does not backfill** existing customers to
`Retail` — that would fabricate a decision nobody made and be indistinguishable from a real one
afterwards.

### Applied only while the bill is empty

The rule lives in `BillPriceLevelPolicy` (Application layer, pure and testable — deliberately not in
the POS ViewModel, where a rule about money could not be tested by this solution):

- **Empty bill** → selecting a customer applies their default, and the selector visibly follows.
- **Populated bill** → the bill keeps its level. Changing the customer says so
  ("*X is a wholesale customer. This bill stays at Retail — switch it above if you want to change.*")
  and changes nothing.

That asymmetry is the point: once lines exist the cashier has quoted those amounts to someone, and
silently re-pricing them because the customer field changed would move a price the customer has
already been told. Removing a customer follows the same rule — an empty bill returns to Retail, a
populated one keeps its level.

The 15B-3 selector remains authoritative: an explicit choice is never undone by a customer
preference.

### Not a sale authority

`SaleService` prices from `CompleteSaleRequest.PriceLevel` and **never reads the customer record**.
A wholesale-default customer on a bill submitted as Retail is charged Retail, and vice versa — so a
customer preference cannot become a way to obtain cheaper prices. Missing-level behaviour is
unchanged: a wholesale-default customer still cannot buy a product that has no wholesale price.

Changing a customer's default is inert with respect to everything already recorded: historical
sales, snapshots, product prices, stock and payments are untouched. It is audited through the
existing `CustomerUpdated` entry, which now names the level it moved from and to; re-saving the same
value claims no change, and merely selecting a customer at the till writes nothing at all.

### A preference, not a history

`Customer.DefaultPriceLevel` is a **current preference**, not a record of how past bills were
priced — a customer switched from Wholesale to Retail today says nothing about last month's
invoices. What each bill was actually sold at is recorded separately on the sale; see below.

## Historical price-level persistence (Phase 15B-5)

`Sale.PriceLevel` records the level a completed bill was actually sold at, so the system can answer
"how much did we sell at wholesale?" without guessing.

### Bill-wide, and written once

The level is stored on **`Sale`**, not `SaleItem`, because that is how it is chosen — one level per
bill, no mixed-level lines, so a sale has exactly one. It is written at completion from the same
`CompleteSaleRequest.PriceLevel` the prices were resolved from, which means the recorded context can
never disagree with the amounts charged.

```
Customer.DefaultPriceLevel   (a current default)
        ↓
Bill.PriceLevel              (what the operator chose)
        ↓
CompleteSaleRequest.PriceLevel
        ↓
Sale.PriceLevel              (historical fact)
```

Nothing downstream recomputes it. `SaleService` never reads the customer, and no code path mutates a
`Sale` after creation — sales were already effectively immutable, and this column inherits that.

### Two different kinds of historical truth

| | Records |
| --- | --- |
| `SaleItem.UnitPriceSnapshot` | what each line **cost** |
| `Sale.PriceLevel` | the **context** it was priced under |

Both are snapshots. Changing today's prices, or a customer's preference, moves neither.

### Existing sales are labelled Retail

The migration backfills every pre-existing sale to `Retail`.

**This is a labelling policy, not a finding.** Those sales never stored their pricing context, and it
cannot be reconstructed afterwards — comparing a snapshot against today's `ProductPrice`, the
customer's current preference, or the product's projection columns would all be guesses against
values that have since moved. Retail is chosen because it is what the till actually did for every one
of those bills (price-level selection did not exist yet), so the label is accurate in practice while
still not being evidence.

> One concrete example from this repository's own development database: the wholesale sale completed
> during Phase 15B-3 verification (INV-2026-000042) is labelled `Retail` by the backfill, because at
> the time it was created there was nowhere to record that it was a wholesale bill. That is exactly
> the limitation this policy describes.

EF scaffolded the column with `defaultValue: ""`, which would have written a value that is not a
member of the enum into every historical row; it was corrected to `Retail` before the migration was
ever applied.

### Reporting

`SalesReportSummary` gains `RetailSales` / `WholesaleSales` and their bill counts, summed from the
level **recorded on each sale**. They reconcile exactly: `RetailSales + WholesaleSales == GrossSales`
for any filter that does not itself narrow by level. `ReportFilter.PriceLevel` narrows to one level.

On screen this surfaces on **Reports → Sales & GST**: two metric cards (`Retail Sales` /
`Wholesale Sales`, each with its bill count) alongside the existing summary, and a `Price level`
filter offering *All price levels* / *Retail* / *Wholesale*.

The filter narrows the **sales summary only** — the GST report on the same screen deliberately
ignores it. GST is owed on everything sold in the period regardless of which level it was billed at,
so a level-filtered GST figure would be a number nobody should be filing. When a level is selected
the exported subtitle says so ("Today — Wholesale only"), because once a CSV leaves the app a
filtered "Gross Sales" is otherwise indistinguishable from an unfiltered one.

Reporting is read-only and reuses the existing `ReportsView` permission — no new permission, and no
separate gate for the level split. Totals, discounts and GST come from the stored sale figures and
are never recalculated from current prices.

`Price Level` is also **appended** to the Sales export (a trailing column, so anything reading it by
position keeps working) and shown on the invoice detail screen. The customer-facing invoice is
deliberately unchanged: which price level a shop billed at is internal information, and there is no
existing convention for printing a pricing category on a receipt.

### Query cost

One resolver call per **distinct product** per sale, made before any write — a cart with the same
item on several lines costs one query, not one per line. The cart's own display resolution is one
query per line added. No caching and no batch API; if profiling later shows either is needed, that
is a deliberate decision to take then rather than a speculative one now.

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

Phase 15B-1 provided resolution only; 15B-2 put it behind billing, 15B-3 added the POS selector,
15B-4 the customer default and 15B-5 the historical record. As of 15B-5 the following are still
**not** included:

- customer-specific or customer-group pricing
- quantity, slab or bulk pricing
- promotions, coupons, loyalty or combo pricing
- scheduled or time-bound pricing
- margin enforcement or below-cost blocking
- changes to price override behaviour
- caching or batch resolution

## Next

Billing, the POS selector, the customer default and the historical record are all in place, so the
next pricing work is the first one that makes a price depend on something other than the level the
operator picked — customer-specific pricing or quantity breaks. Both are additive: they extend
`PricingContext` and add a `PriceSource` member rather than changing the resolver's signature or any
call site, which is what the context type was introduced for.

Retiring `Product.SellingPrice` / `Product.WholesalePrice` remains outstanding. Nothing bills through
them any more, but reports, exports and label printing still read the projections, so removing them
is its own migration rather than a side effect of a pricing phase.
