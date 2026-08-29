using Kirana.Domain.Entities;

namespace Kirana.Application.Reports;

/// <summary>Filters shared by the Sales, Product and Inventory report screens (PRD §51 "Search &amp;
/// Filters"). Every field is optional — an unset filter simply does not narrow the query.</summary>
public sealed class ReportFilter
{
    public int? CustomerId { get; init; }
    public int? SupplierId { get; init; }
    public int? ProductId { get; init; }
    public int? CategoryId { get; init; }
    public int? BrandId { get; init; }
    public Domain.Entities.PaymentMethod? PaymentMethod { get; init; }
    public int? UserId { get; init; }

    /// <summary>Narrows to bills sold at one price level (Phase 15B-5). Reads the level RECORDED on
    /// the sale — never today's product prices.</summary>
    public PriceLevel? PriceLevel { get; init; }
}

public sealed class SalesReportSummary
{
    public required ReportDateRange Range { get; init; }

    public decimal GrossSales { get; init; }
    public decimal Returns { get; init; }
    public decimal NetSales { get; init; }
    public decimal ItemDiscounts { get; init; }
    public decimal BillDiscounts { get; init; }
    public decimal TotalDiscounts { get; init; }
    public decimal GstCollected { get; init; }
    public int BillCount { get; init; }
    public decimal AverageBillValue { get; init; }
    public decimal ItemsSold { get; init; }

    // ---- Price level split (Phase 15B-5) ----
    //
    // Summed from the level RECORDED on each sale, never reconstructed from current prices. Both
    // figures are gross (they sum GrandTotal), so RetailSales + WholesaleSales == GrossSales for
    // any filter that does not itself narrow by level.

    public decimal RetailSales { get; init; }
    public decimal WholesaleSales { get; init; }
    public int RetailBillCount { get; init; }
    public int WholesaleBillCount { get; init; }

    public IReadOnlyList<PaymentMethodAmount> PaymentMethodBreakdown { get; init; } = [];
}

public sealed class PaymentMethodAmount
{
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// One GST-rate bucket in a historical GST summary. Resolved intra-state tax is allocated to
/// <see cref="Cgst"/>/<see cref="Sgst"/>, inter-state tax to <see cref="Igst"/>, and tax lacking
/// sufficient historical state evidence to <see cref="UnresolvedGst"/>.
/// </summary>
public sealed class GstRateBreakdown
{
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal Igst { get; init; }
    public decimal UnresolvedGst { get; init; }
    public int InvoiceCount { get; init; }

    /// <summary>Phase 18A-6: stored taxable value split by historical party classification within
    /// this rate slab. Sales use the Phase 18A-4 transaction class; purchases map
    /// RegisteredSupplier onto the B2b slot and UnregisteredSupplier onto the B2c slot. The three
    /// amounts always sum to <see cref="TaxableAmount"/>.</summary>
    public decimal B2bTaxableAmount { get; init; }
    public decimal B2cTaxableAmount { get; init; }
    public decimal UnresolvedIdentityTaxableAmount { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
    public string GstTreatment => PricingType == PricingType.Inclusive ? "GST Included" : "GST Added";
}

/// <summary>
/// GST collected on sales and paid on purchases for a date range (PRD §51 "GST Reports"), built
/// entirely from the historical <c>GstRatePercentSnapshot</c>/<c>TaxableAmount</c>/<c>GstAmount</c>
/// fields captured on each <c>SaleItem</c>/<c>PurchaseItem</c> at the time of the transaction —
/// never from a product's current GST rate.
///
/// Jurisdiction is resolved from immutable StateCode snapshots captured on each transaction.
/// Legacy, walk-in, missing, or invalid historical evidence remains explicitly unresolved;
/// current customer, supplier, and store masters are never used as substitutes.
/// </summary>
public sealed class GstReport
{
    public required ReportDateRange Range { get; init; }

    public decimal SalesTaxableAmount { get; init; }
    public decimal SalesGstCollected { get; init; }
    public IReadOnlyList<GstRateBreakdown> SalesByRate { get; init; } = [];

    /// <summary>Phase 18A-5: stored sales GST split by the historical party classification
    /// (Phase 18A-4). The three amounts always sum to <see cref="SalesGstCollected"/>.</summary>
    public decimal SalesB2bGst { get; init; }
    public decimal SalesB2cGst { get; init; }
    public decimal SalesUnresolvedIdentityGst { get; init; }

    public decimal PurchaseTaxableAmount { get; init; }
    public decimal PurchaseGstPaid { get; init; }
    public IReadOnlyList<GstRateBreakdown> PurchasesByRate { get; init; } = [];

    /// <summary>Phase 18A-5: stored purchase GST split by the historical supplier classification.
    /// Purchases keep supplier terminology rather than B2C labels. The three amounts always sum
    /// to <see cref="PurchaseGstPaid"/>.</summary>
    public decimal PurchaseRegisteredSupplierGst { get; init; }
    public decimal PurchaseUnregisteredSupplierGst { get; init; }
    public decimal PurchaseUnresolvedSupplierGst { get; init; }

    // --- Phase 18A-6: jurisdiction taxable-value totals (stored snapshots only) ---
    public decimal SalesIntraStateTaxableValue { get; init; }
    public decimal SalesInterStateTaxableValue { get; init; }
    public decimal SalesUnresolvedJurisdictionTaxableValue { get; init; }
    public decimal PurchaseIntraStateTaxableValue { get; init; }
    public decimal PurchaseInterStateTaxableValue { get; init; }
    public decimal PurchaseUnresolvedJurisdictionTaxableValue { get; init; }

    // --- Phase 18A-6: classification taxable-value totals (mirror the GST trios above) ---
    public decimal SalesB2bTaxableValue { get; init; }
    public decimal SalesB2cTaxableValue { get; init; }
    public decimal SalesUnresolvedIdentityTaxableValue { get; init; }
    public decimal PurchaseRegisteredSupplierTaxableValue { get; init; }
    public decimal PurchaseUnregisteredSupplierTaxableValue { get; init; }
    public decimal PurchaseUnresolvedSupplierTaxableValue { get; init; }

    /// <summary>Phase 18A-6: distinct completed bills in the report window, overall and per
    /// historical classification. A bill sold across several rate slabs is counted once.</summary>
    public int SalesBillCount { get; init; }
    public int SalesB2bBillCount { get; init; }
    public int SalesB2cBillCount { get; init; }
    public int SalesUnresolvedBillCount { get; init; }
    public int PurchaseBillCount { get; init; }

    /// <summary>Phase 18A-6: GST reversal for returns dated inside the report window. Reversal is
    /// proportional to the returned quantity against each originating line's stored taxable/GST —
    /// never today's product data. Net = gross − returned.</summary>
    public decimal SalesReturnedTaxableValue { get; init; }
    public decimal SalesReturnedGst { get; init; }
    public decimal NetSalesTaxableValue => SalesTaxableAmount - SalesReturnedTaxableValue;
    public decimal NetSalesGst => SalesGstCollected - SalesReturnedGst;
}
