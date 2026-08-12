using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Create Purchase screen (PRD §28) — reuses the same product search/barcode
/// pipeline as POS billing, but prices lines with the negotiated purchase price rather than the
/// product's selling price.</summary>
public sealed partial class PurchaseEntryViewModel(
    IProductService productService,
    IBarcodeLookupService barcodeLookupService,
    ISupplierService supplierService,
    IPurchaseService purchaseService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupplierDisplayText))]
    [NotifyPropertyChangedFor(nameof(HasSupplier))]
    [NotifyPropertyChangedFor(nameof(SupplierButtonLabel))]
    [NotifyPropertyChangedFor(nameof(SupplierCodeText))]
    [NotifyPropertyChangedFor(nameof(HasSupplierGstin))]
    [NotifyPropertyChangedFor(nameof(SupplierGstin))]
    [NotifyPropertyChangedFor(nameof(HasSupplierPhone))]
    [NotifyPropertyChangedFor(nameof(SupplierPhone))]
    [NotifyPropertyChangedFor(nameof(CurrentOutstanding))]
    [NotifyPropertyChangedFor(nameof(OutstandingAfterPurchase))]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private string _supplierInvoiceNumber = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountPaid))]
    [NotifyPropertyChangedFor(nameof(OutstandingAfterPurchase))]
    [NotifyPropertyChangedFor(nameof(RemainingBalance))]
    private string _amountPaidText = "0";

    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.Upi, PaymentMethod.Card];

    [ObservableProperty]
    private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private string _paymentReferenceNumber = string.Empty;

    [ObservableProperty]
    private decimal _subTotal;

    [ObservableProperty]
    private decimal _discountTotal;

    [ObservableProperty]
    private decimal _taxTotal;

    [ObservableProperty]
    private decimal _roundOffAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingAfterPurchase))]
    [NotifyPropertyChangedFor(nameof(RemainingBalance))]
    private decimal _grandTotal;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public int? CurrentUserId => session.CurrentUser?.Id;

    public string SupplierDisplayText => SelectedSupplier?.Name ?? "Select supplier…";

    public bool HasSupplier => SelectedSupplier is not null;

    public string SupplierButtonLabel => HasSupplier ? "Change" : "Select Supplier";

    public string SupplierCodeText => SelectedSupplier?.SupplierCode ?? string.Empty;

    public string? SupplierGstin => SelectedSupplier?.Gstin;

    public bool HasSupplierGstin => !string.IsNullOrWhiteSpace(SelectedSupplier?.Gstin);

    public string? SupplierPhone => SelectedSupplier?.Phone;

    public bool HasSupplierPhone => !string.IsNullOrWhiteSpace(SelectedSupplier?.Phone);

    /// <summary>Live "what happens to this supplier's balance" preview shown on the supplier card —
    /// purely a UI projection of the same figures already on screen, not a separate calculation the
    /// server relies on (<see cref="IPurchaseService"/> derives the real <c>OutstandingAmount</c>
    /// itself when the purchase is finalized).</summary>
    public decimal CurrentOutstanding => SelectedSupplier?.OutstandingBalance ?? 0;

    public decimal AmountPaid => decimal.TryParse(AmountPaidText, out var v) ? v : 0;

    public decimal OutstandingAfterPurchase => CurrentOutstanding + GrandTotal - AmountPaid;

    public decimal RemainingBalance => GrandTotal - AmountPaid;

    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = [];

    public bool HasLines => Lines.Count > 0;

    [ObservableProperty]
    private bool _hasSuggestions;

    public ObservableCollection<Product> SearchSuggestions { get; } = [];

    private int _suggestionQueryToken;

    public Purchase? CompletedPurchase { get; private set; }

    /// <summary>Same keyboard-wedge scanner pipeline the POS uses (PRD §17) — receiving goods by
    /// scanning each item is exactly as useful as scanning them at checkout.</summary>
    public IScannerInputBuffer ScannerBuffer { get; } = new ScannerInputBuffer();

    private bool _scannerWired;

    public void Initialize()
    {
        if (_scannerWired)
        {
            return;
        }

        _scannerWired = true;
        ScannerBuffer.BarcodeScanned += barcode => _ = HandleBarcodeScannedAsync(barcode);
    }

    public async Task HandleBarcodeScannedAsync(string barcode)
    {
        ErrorMessage = null;
        var product = await barcodeLookupService.LookupAsync(barcode);
        if (product is null)
        {
            ErrorMessage = $"No product found for barcode '{barcode}'.";
            return;
        }

        AddOrIncrement(product);
    }

    public async Task HandleManualSearchAsync(string text)
    {
        ErrorMessage = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var results = await productService.SearchAsync(new ProductSearchQuery { SearchText = trimmed, MaxResults = 1 });
        var product = results.FirstOrDefault();
        if (product is null)
        {
            ErrorMessage = $"No product found for '{trimmed}'.";
            return;
        }

        AddOrIncrement(product);
    }

    /// <summary>Live "as you type" suggestions for the search box, same behavior as the POS billing
    /// screen — reuses the same prioritized <see cref="IProductService.SearchAsync"/> the Enter-to-add
    /// path already uses.</summary>
    public async Task UpdateSuggestionsAsync(string text)
    {
        var token = ++_suggestionQueryToken;
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            SearchSuggestions.Clear();
            HasSuggestions = false;
            return;
        }

        var results = await productService.SearchAsync(new ProductSearchQuery { SearchText = trimmed, MaxResults = 8 });

        if (token != _suggestionQueryToken)
        {
            return;
        }

        SearchSuggestions.Clear();
        foreach (var product in results)
        {
            SearchSuggestions.Add(product);
        }

        HasSuggestions = SearchSuggestions.Count > 0;
    }

    public void ClearSuggestions()
    {
        _suggestionQueryToken++;
        SearchSuggestions.Clear();
        HasSuggestions = false;
    }

    public void AddOrIncrement(Product product)
    {
        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.QuantityText = (existing.Quantity + 1).ToString("0.###");
        }
        else
        {
            Lines.Add(new PurchaseLineViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Sku = product.Sku,
                Unit = product.Unit.ToString(),
                SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
                TracksBatches = product.TracksBatches,
                GstRatePercent = product.GstRatePercent ?? 0,
                PricingType = product.PricingType,
                PurchasePackUnit = product.PurchasePackUnit,
                PurchasePackSize = product.PurchasePackSize,
                QuantityText = "1",
                UnitPriceText = product.PurchasePrice.ToString("0.##"),
            });
        }

        RecalculateTotals();
    }

    public void RemoveLine(PurchaseLineViewModel line)
    {
        Lines.Remove(line);
        RecalculateTotals();
    }

    public void RecalculateTotals()
    {
        OnPropertyChanged(nameof(HasLines));

        // Totals refresh on every keystroke, so a half-typed quantity is a normal, expected state:
        // clearing the box to retype "10" passes through empty (quantity 0) first. Hold the last
        // good totals instead of flashing "quantity must be positive" as the user types —
        // OnLineFieldLostFocus clamps the value when focus leaves, and PurchaseService re-validates
        // everything at finalisation, so nothing invalid can actually be recorded. Mirrors
        // PosShellViewModel.RecalculateCart.
        if (Lines.Any(l => l.EffectiveQuantity <= 0))
        {
            return;
        }

        var purchaseLines = Lines.Select(l => new PurchaseLine
        {
            ProductId = l.ProductId,
            Quantity = l.EffectiveQuantity,
            UnitPrice = l.UnitPrice,
            PricingType = l.PricingType,
            GstRatePercent = l.GstRatePercent,
            DiscountPercent = l.DiscountPercent,
        }).ToList();

        if (purchaseLines.Count == 0)
        {
            SubTotal = DiscountTotal = TaxTotal = RoundOffAmount = GrandTotal = 0;
            return;
        }

        PurchaseTotals totals;
        try
        {
            totals = PurchasePricingCalculator.Calculate(purchaseLines);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        foreach (var result in totals.Lines)
        {
            var line = Lines.First(l => l.ProductId == result.Line.ProductId);
            line.TaxableAmount = result.TaxableAmount;
            line.GstAmount = result.GstAmount;
            line.LineTotal = result.LineTotal;
        }

        SubTotal = totals.SubTotal;
        DiscountTotal = totals.DiscountTotal;
        TaxTotal = totals.TaxTotal;
        RoundOffAmount = totals.RoundOffAmount;
        GrandTotal = totals.GrandTotal;
    }

    public Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(string? text) =>
        supplierService.SearchAsync(new SupplierSearchQuery { SearchText = text }, CurrentUserId);

    [RelayCommand]
    private async Task CompletePurchaseAsync()
    {
        ErrorMessage = null;

        if (SelectedSupplier is null)
        {
            ErrorMessage = "Select a supplier.";
            return;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = "Add at least one product line.";
            return;
        }

        var amountPaid = decimal.TryParse(AmountPaidText, out var paid) ? paid : 0;

        IsBusy = true;
        try
        {
            var request = new CreatePurchaseRequest
            {
                SupplierId = SelectedSupplier.Id,
                SupplierInvoiceNumber = string.IsNullOrWhiteSpace(SupplierInvoiceNumber) ? null : SupplierInvoiceNumber,
                Lines = Lines.Select(l => new PurchaseLineInput
                {
                    ProductId = l.ProductId,
                    Quantity = l.EffectiveQuantity,
                    UnitPrice = l.UnitPrice,
                    DiscountPercent = l.DiscountPercent,
                    PricingType = l.PricingType,
                    BatchNumber = string.IsNullOrWhiteSpace(l.BatchNumberText) ? null : l.BatchNumberText,
                    ExpiryDate = l.ExpiryDate,
                    PurchasedPackUnit = l.IsPackMode ? l.PurchasePackUnit : null,
                    PurchasedPackQuantity = l.IsPackMode ? l.PackQuantity : null,
                }).ToList(),
                AmountPaid = amountPaid,
                PaymentMethod = amountPaid > 0 ? SelectedPaymentMethod : (PaymentMethod?)null,
                PaymentReferenceNumber = string.IsNullOrWhiteSpace(PaymentReferenceNumber) ? null : PaymentReferenceNumber,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
                CreatedByUserId = CurrentUserId,
            };

            CompletedPurchase = await purchaseService.FinalizePurchaseAsync(request);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
