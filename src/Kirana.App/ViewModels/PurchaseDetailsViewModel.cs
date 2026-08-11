using System.Collections.ObjectModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Read-only flattening of a <see cref="Purchase"/> for the Purchase Details dialog.
/// Pure presentation — same <c>PurchasePaymentStatus</c> classification the Purchases list already
/// uses, so the badge here can never disagree with the one on the row it was opened from.</summary>
public sealed class PurchaseDetailsViewModel
{
    // Plain (non-required) init properties: a `required` member on any type x:Bind can reach forces
    // the XAML compiler to generate a parameterless activator for it, which then fails to build
    // (documented WinUI3 gotcha) — FromPurchase below is the only real construction path anyway.
    public string PurchaseNumber { get; init; } = "";
    public string SupplierName { get; init; } = "";
    public string SupplierCode { get; init; } = "";
    public string DateText { get; init; } = "";
    public string? SupplierInvoiceNumber { get; init; }
    public string? Notes { get; init; }

    public decimal SubTotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal OutstandingAmount { get; init; }

    public ObservableCollection<PurchaseDetailsLineViewModel> Lines { get; } = [];

    public bool HasSupplierInvoiceNumber => !string.IsNullOrWhiteSpace(SupplierInvoiceNumber);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasDiscount => DiscountTotal > 0;
    public bool HasOutstanding => OutstandingAmount > 0;

    public string SupplierCodeParenthesized => $"({SupplierCode})";

    public string SupplierInitial => string.IsNullOrWhiteSpace(SupplierName)
        ? "?"
        : SupplierName.TrimStart()[0].ToString().ToUpperInvariant();

    public PurchasePaymentStatus PaymentStatus => OutstandingAmount <= 0
        ? PurchasePaymentStatus.Paid
        : AmountPaid > 0
            ? PurchasePaymentStatus.PartiallyPaid
            : PurchasePaymentStatus.Outstanding;

    public bool IsPaid => PaymentStatus == PurchasePaymentStatus.Paid;
    public bool IsPartiallyPaid => PaymentStatus == PurchasePaymentStatus.PartiallyPaid;
    public bool IsFullyOutstanding => PaymentStatus == PurchasePaymentStatus.Outstanding;

    public static PurchaseDetailsViewModel FromPurchase(Purchase purchase)
    {
        var vm = new PurchaseDetailsViewModel
        {
            PurchaseNumber = purchase.PurchaseNumber,
            SupplierName = purchase.Supplier.Name,
            SupplierCode = purchase.Supplier.SupplierCode,
            DateText = purchase.PurchaseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"),
            SupplierInvoiceNumber = purchase.SupplierInvoiceNumber,
            Notes = purchase.Notes,
            SubTotal = purchase.SubTotal,
            DiscountTotal = purchase.DiscountTotal,
            TaxTotal = purchase.TaxTotal,
            RoundOffAmount = purchase.RoundOffAmount,
            GrandTotal = purchase.GrandTotal,
            AmountPaid = purchase.AmountPaid,
            OutstandingAmount = purchase.OutstandingAmount,
        };

        foreach (var item in purchase.Items)
        {
            vm.Lines.Add(new PurchaseDetailsLineViewModel
            {
                ProductName = item.ProductNameSnapshot,
                ProductCode = item.ProductCodeSnapshot,
                QuantityText = item.Quantity.ToString("0.###"),
                UnitText = item.UnitSnapshot,
                UnitPrice = item.PurchasePriceSnapshot,
                LineTotal = item.LineTotal,
                BatchNumber = item.BatchNumber,
            });
        }

        return vm;
    }
}
