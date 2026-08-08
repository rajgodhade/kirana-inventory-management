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

    public IReadOnlyList<PaymentMethodAmount> PaymentMethodBreakdown { get; init; } = [];
}

public sealed class PaymentMethodAmount
{
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

/// <summary>One GST-rate bucket in a GST summary. <see cref="Cgst"/>/<see cref="Sgst"/> split the
/// tax amount evenly and <see cref="Igst"/> is always zero — see the class doc on
/// <see cref="GstReport"/> for why.</summary>
public sealed class GstRateBreakdown
{
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal Igst { get; init; }
    public int InvoiceCount { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
    public string GstTreatment => PricingType == PricingType.Inclusive ? "GST Included" : "GST Added";
}

/// <summary>
/// GST collected on sales and paid on purchases for a date range (PRD §51 "GST Reports"), built
/// entirely from the historical <c>GstRatePercentSnapshot</c>/<c>TaxableAmount</c>/<c>GstAmount</c>
/// fields captured on each <c>SaleItem</c>/<c>PurchaseItem</c> at the time of the transaction —
/// never from a product's current GST rate.
///
/// <b>CGST/SGST/IGST assumption:</b> Kirana does not record which state a customer or supplier is
/// in, only the store's own state (<c>Store.State</c>), so there is no data to determine whether
/// any given transaction was intra-state or inter-state. Every transaction is therefore treated as
/// intra-state — the tax amount is split evenly into CGST and SGST, and IGST is always reported as
/// zero. This matches the overwhelmingly common case for a local kirana store and is called out
/// explicitly here and in the report's UI rather than silently guessed at.
/// </summary>
public sealed class GstReport
{
    public required ReportDateRange Range { get; init; }

    public decimal SalesTaxableAmount { get; init; }
    public decimal SalesGstCollected { get; init; }
    public IReadOnlyList<GstRateBreakdown> SalesByRate { get; init; } = [];

    public decimal PurchaseTaxableAmount { get; init; }
    public decimal PurchaseGstPaid { get; init; }
    public IReadOnlyList<GstRateBreakdown> PurchasesByRate { get; init; } = [];
}
