# Historical Cost Basis (Phase 17A)

## The problem

Profit was calculated like this:

```
COGS = Σ (SaleItem.Quantity × SaleItem.Product.PurchasePrice)
```

`Product.PurchasePrice` is **current master data**. It was read at *report* time, not at sale time, so:

> Sell 10 units at a cost of ₹100 → COGS ₹1,000.
> Raise the product's purchase price to ₹130 tomorrow → **the same historical report now says ₹1,300.**

Past profit moved whenever a cost was updated. Two runs of the same dated report could disagree, and no supplier price rise could be recorded without silently rewriting history.

This was the last place in the application still reconstructing a historical fact from current master data. Every other snapshot — selling price, MRP, GST rate, HSN, discount, product name, price level — had already been captured on the transaction.

## What was added

`SaleItem.UnitCostSnapshot` — `decimal(18,2)`, **nullable**.

It records what one unit cost the shop *at the moment it was sold*, and sits directly alongside `UnitPriceSnapshot`, which records what it was sold for. Cost and price are two separate historical facts about the same line.

Migration: `20260817120000_AddSaleItemUnitCostSnapshot`. Additive column only — no data transformation, no rewrite of any existing row.

## Costing policy

**The product's purchase price at the time of sale.**

This is the honest minimum, and it is what the till actually knows. `SaleService` never consults `ProductBatch.PurchasePrice` — batch selection is not part of the POS path — so `Product.PurchasePrice` is the only authoritative cost available when a sale completes. Choosing anything else would have meant inventing a cost source the sale never used.

Explicitly **not** implemented here: weighted-average, FIFO, LIFO, batch costing, supplier-specific costing, landed cost, and any automatic recalculation. Those are a **Phase 17C** decision, and they are a decision — not a detail — because they change what the reported margin means.

## `NULL` means "unknown", never "zero"

Every sale recorded before this phase has `NULL`.

That is a real state and it is preserved as one:

- **Not defaulted to 0.** A zero cost reports the line at 100% margin. That is not a conservative fallback; it is a wrong number wearing the costume of a right one.
- **Not backfilled from the product.** Today's purchase price is not evidence of what the shop paid a year ago. Worse, once written it would be indistinguishable from a genuinely captured cost — the fabrication would be permanent and invisible.

The profit report therefore **excludes** null-cost lines from COGS and reports how many it excluded:

```
ProfitSummary.KnownCostLineCount
ProfitSummary.UnknownCostLineCount
ProfitSummary.HasCompleteCostBasis
```

While `UnknownCostLineCount > 0`, those lines contribute revenue but no cost, so the gross profit shown is an **upper bound**. The Profit screen states this in a warning banner naming the counts; when every line is costed it states the basis plainly instead. The old "Est." prefixes on Cost of Goods Sold, Gross Profit and Net Profit were removed, because for a complete-basis period the figures are no longer estimates.

> The Dashboard's KPI tiles still read "EST. GROSS PROFIT" / "EST. NET PROFIT". They share this service and inherit the fix, but a tile has nowhere to show the unknown-cost disclosure — so the hedge stays there deliberately.

## The invariant

```
Sell today            → UnitCostSnapshot = today's purchase cost
Change the cost later → the stored snapshot does not move
Re-run an old report  → historical COGS does not move
```

This is the whole point of the phase. `HistoricalCostBasisTests.HistoricalCogs_DoesNotMove_WhenTheProductIsRepricedLater` pins it, repricing twice so a merely-lagging implementation fails too.

## Returns

`SalesReturnItem` carries **no cost of its own**. Returned quantities net off at the originating `SaleItem.UnitCostSnapshot`, reached through the `SaleItem` navigation.

This is deliberate for 17A: it keeps a return on exactly the same historical basis as the sale it reverses, without introducing a second cost record that could diverge from it. A return against an unknown-cost sale contributes no cost credit at all, rather than a phantom one at today's price.

**Phase 17B** should decide whether a return needs its own snapshot. It would only matter if returns can ever be processed against something other than an original sale line; while every return points at a `SaleItem`, the navigation is sufficient and a second column would be duplication.

## Known limitations

1. **Historical sales have no cost.** Permanent and intentional; surfaced rather than hidden.
2. **One costing basis.** Purchase-price-at-sale-time only. A shop buying the same item at different prices sees the cost prevailing when it sold, not what that particular unit cost.
3. **Batch cost is ignored** even where `ProductBatch.PurchasePrice` exists, because the POS does not select batches at sale time.
4. **Inventory valuation still uses current cost** (`DashboardService`). That is correct — valuing stock you *hold* at what it would cost *now* is a different question from what sold goods cost — and is not affected by this phase.

## Later phases

- **17B** — return cost handling, if the navigation-based approach proves insufficient.
- **17C** — weighted-average / FIFO / batch costing, as an explicit product decision.
