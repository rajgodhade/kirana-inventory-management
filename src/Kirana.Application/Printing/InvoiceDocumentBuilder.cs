using Kirana.Domain.Entities;
using Kirana.Application.Taxation;

namespace Kirana.Application.Printing;

public sealed class InvoiceDocumentBuilder(IGstCalculationService? gstCalculationService = null) : IInvoiceDocumentBuilder
{
    public InvoiceDocument Build(Sale sale, Store store)
    {
        var lines = sale.Items.Select(item => new InvoiceLine
        {
            ProductName = item.ProductNameSnapshot,
            ProductCode = item.ProductCodeSnapshot,
            Sku = item.SkuSnapshot,
            HsnCode = item.HsnCodeSnapshot,
            Unit = item.UnitSnapshot,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPriceSnapshot,
            Mrp = item.MrpSnapshot,
            DiscountPercent = item.DiscountPercent,
            DiscountAmount = item.DiscountAmount,
            PromotionDiscountAmount = item.PromotionDiscountAmount,
            PromotionText = string.Join(" + ", item.Promotions.Select(x => x.PromotionNameSnapshot)),
            GstRatePercent = item.GstRatePercentSnapshot,
            PricingType = item.IsTaxInclusiveSnapshot ? PricingType.Inclusive : PricingType.Exclusive,
            TaxableAmount = item.TaxableAmount,
            GstAmount = item.GstAmount,
            LineTotal = item.LineTotal,
        }).ToList();

        var snapshots = sale.Items.Select(item => new GstSnapshotLine
        {
            TransactionId = sale.Id,
            RatePercent = item.GstRatePercentSnapshot,
            TaxableAmount = item.TaxableAmount,
            GstAmount = item.GstAmount,
            PricingType = item.IsTaxInclusiveSnapshot ? PricingType.Inclusive : PricingType.Exclusive,
        }).ToList();
        var calculator = gstCalculationService ?? GstCalculationService.Shared;
        calculator.ValidateStored(snapshots, new GstStoredTotals
        {
            TaxableTotal = sale.TaxableTotal,
            GstTotal = sale.TaxTotal,
            RoundOffAmount = sale.RoundOffAmount,
            GrandTotal = sale.GrandTotal,
        }, $"invoice {sale.InvoiceNumber}");

        var gstGroups = calculator.SummarizeStored(snapshots)
            .Select(group => new InvoiceGstGroup
            {
                RatePercent = group.RatePercent,
                TaxableAmount = group.TaxableAmount,
                GstAmount = group.GstAmount,
                PricingType = group.PricingType,
            })
            .ToList();

        // Compared against what was actually charged for the whole bill (GrandTotal), not summed
        // per line before tax/discount — MRP is inherently tax-inclusive by law, and GrandTotal is
        // the one number that already nets out item discounts, bill discount, tax and rounding, so
        // it's the correct like-for-like comparison. Floored at zero: a price override that pushed
        // a line above its own MRP must never show as a negative "savings".
        var mrpTotal = sale.Items.Sum(item => item.MrpSnapshot * item.Quantity);
        var totalSavings = Math.Max(0, mrpTotal - sale.GrandTotal);

        var payments = sale.Payments.Select(payment => new InvoicePaymentLine
        {
            Method = payment.Method,
            Amount = payment.Amount,
            ReferenceNumber = payment.ReferenceNumber,
            AmountTendered = payment.AmountTendered,
            ChangeGiven = payment.ChangeGiven,
        }).ToList();

        return new InvoiceDocument
        {
            SaleId = sale.Id,
            StoreName = store.Name,
            StoreAddress = BuildStoreAddress(store),
            StoreContactNumber = store.ContactNumber,
            StoreGstin = store.Gstin,
            StoreLogoPath = store.LogoPath,
            FooterText = store.InvoiceFooterText,

            InvoiceNumber = sale.InvoiceNumber,
            SaleDateUtc = sale.SaleDateUtc,
            CashierName = sale.CashierUser?.FullName,
            CustomerName = sale.Customer?.Name,
            CustomerPhone = sale.Customer?.Phone,
            CustomerGstin = sale.Customer?.Gstin,

            Lines = lines,
            Payments = payments,
            PaymentSummaryLines = PaymentSummaryBuilder.Build(payments),
            GstGroups = gstGroups,

            SubTotal = sale.SubTotal,
            ItemDiscountTotal = sale.ItemDiscountTotal,
            PromotionDiscountTotal = sale.PromotionDiscountTotal,
            BillDiscountPercent = sale.BillDiscountPercent,
            BillDiscountAmount = sale.BillDiscountAmount,
            TaxTotal = sale.TaxTotal,
            RoundOffAmount = sale.RoundOffAmount,
            GrandTotal = sale.GrandTotal,
            TotalSavings = totalSavings,
        };
    }

    private static string? BuildStoreAddress(Store store)
    {
        var parts = new[] { store.Address, store.City, store.State, store.PinCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var combined = string.Join(", ", parts);
        return combined.Length == 0 ? null : combined;
    }
}
