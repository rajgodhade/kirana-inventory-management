using Kirana.Domain.Entities;

namespace Kirana.Application.Printing;

public sealed class InvoiceDocumentBuilder : IInvoiceDocumentBuilder
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
            DiscountPercent = item.DiscountPercent,
            DiscountAmount = item.DiscountAmount,
            GstRatePercent = item.GstRatePercentSnapshot,
            TaxableAmount = item.TaxableAmount,
            GstAmount = item.GstAmount,
            LineTotal = item.LineTotal,
        }).ToList();

        var gstGroups = sale.Items
            .GroupBy(item => item.GstRatePercentSnapshot)
            .Where(g => g.Key != 0)
            .Select(g => new InvoiceGstGroup
            {
                RatePercent = g.Key,
                TaxableAmount = g.Sum(i => i.TaxableAmount),
                GstAmount = g.Sum(i => i.GstAmount),
            })
            .OrderBy(g => g.RatePercent)
            .ToList();

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
            GstGroups = gstGroups,

            SubTotal = sale.SubTotal,
            ItemDiscountTotal = sale.ItemDiscountTotal,
            BillDiscountPercent = sale.BillDiscountPercent,
            BillDiscountAmount = sale.BillDiscountAmount,
            TaxTotal = sale.TaxTotal,
            RoundOffAmount = sale.RoundOffAmount,
            GrandTotal = sale.GrandTotal,
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
