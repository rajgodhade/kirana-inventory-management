using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the production POS billing screen (PRD §18-20) — the app's default screen. Reuses
/// Phase 2/3 product search, barcode lookup, and Phase 1 permission/session infrastructure;
/// pricing/GST math is delegated entirely to <see cref="CartPricingCalculator"/> and persistence
/// to <see cref="ISaleService"/>/<see cref="IHeldBillService"/> so this class stays UI glue.
/// </summary>
public sealed partial class PosShellViewModel(
    IProductService productService,
    IBarcodeLookupService barcodeLookupService,
    IHeldBillService heldBillService,
    ICustomerService customerService,
    IKiranaDbContext db,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _storeName = "Kirana";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerDisplayText))]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private decimal _billDiscountPercent;

    [ObservableProperty]
    private decimal _subTotal;

    [ObservableProperty]
    private decimal _itemDiscountTotal;

    [ObservableProperty]
    private decimal _billDiscountAmount;

    [ObservableProperty]
    private decimal _taxTotal;

    [ObservableProperty]
    private decimal _roundOffAmount;

    [ObservableProperty]
    private decimal _grandTotal;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeldBillsSummary))]
    [NotifyPropertyChangedFor(nameof(HasHeldBills))]
    private int _heldBillsCount;

    public bool IsGstEnabledForStore { get; private set; }

    public InvoiceFormat DefaultInvoiceFormat { get; private set; } = InvoiceFormat.Thermal80mm;

    public int? DiscountAuthorizedByUserId { get; private set; }

    public int? PriceOverrideAuthorizedByUserId { get; private set; }

    public string CustomerDisplayText => SelectedCustomer?.Name ?? "Walk-in Customer";

    public bool HasHeldBills => HeldBillsCount > 0;

    public string HeldBillsSummary => HeldBillsCount switch
    {
        0 => "No held bills",
        1 => "1 held bill",
        _ => $"{HeldBillsCount} held bills",
    };

    public int? CashierUserId => session.CurrentUser?.Id;

    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];

    [ObservableProperty]
    private bool _hasSuggestions;

    public ObservableCollection<Product> SearchSuggestions { get; } = [];

    public IScannerInputBuffer ScannerBuffer { get; } = new ScannerInputBuffer();

    private bool _scannerWired;
    private int _suggestionQueryToken;

    public async Task InitializeAsync()
    {
        if (!_scannerWired)
        {
            _scannerWired = true;
            ScannerBuffer.BarcodeScanned += barcode => _ = HandleBarcodeScannedAsync(barcode);
        }

        var store = await db.Stores.FirstOrDefaultAsync();
        if (store is not null)
        {
            StoreName = store.Name;
            IsGstEnabledForStore = store.IsGstEnabled;
            DefaultInvoiceFormat = InvoiceLayoutCalculator.ParseFormat(store.DefaultInvoiceFormat);
        }

        await RefreshHeldBillsCountAsync();
    }

    public async Task RefreshHeldBillsCountAsync() =>
        HeldBillsCount = (await heldBillService.GetHeldBillsAsync()).Count;

    /// <summary>Fast path for a scanner burst — an exact barcode, nothing else.</summary>
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

    /// <summary>Manual search box Enter — Product ID / SKU / name (PRD §13), takes the
    /// highest-priority match exactly like the Products page search.</summary>
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

    /// <summary>Live "as you type" suggestions for the search box (e.g. typing "Amu" surfaces every
    /// product whose name, code, SKU, or barcode contains it) — reuses the same prioritized
    /// <see cref="IProductService.SearchAsync"/> the Enter-to-add path and the Products page already
    /// use, so suggestions and the eventual match are always consistent with each other.</summary>
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

        // A newer keystroke's query may have already started (and could finish first) — drop this
        // result if it is no longer the latest request, so a slow early query can't clobber a
        // faster later one's suggestions.
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
        var existing = CartLines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.QuantityText = (existing.Quantity + 1).ToString("0.###");
        }
        else
        {
            CartLines.Add(new CartLineViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Sku = product.Sku,
                Unit = product.Unit.ToString(),
                SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
                OriginalUnitPrice = product.SellingPrice,
                UnitPriceText = product.SellingPrice.ToString("0.##"),
                GstRatePercent = product.GstRatePercent ?? 0,
                IsTaxInclusive = product.IsTaxInclusive,
                QuantityText = "1",
            });
        }

        RecalculateCart();
    }

    public void RemoveLine(CartLineViewModel line)
    {
        CartLines.Remove(line);
        RecalculateCart();
    }

    public void RecalculateCart()
    {
        var cartLines = CartLines.Select(l => new CartLine
        {
            ProductId = l.ProductId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            IsTaxInclusive = l.IsTaxInclusive,
            GstRatePercent = l.GstRatePercent,
            DiscountPercent = l.DiscountPercent,
        }).ToList();

        if (cartLines.Count == 0)
        {
            SubTotal = ItemDiscountTotal = BillDiscountAmount = TaxTotal = RoundOffAmount = GrandTotal = 0;
            return;
        }

        CartTotals totals;
        try
        {
            totals = CartPricingCalculator.Calculate(cartLines, BillDiscountPercent, IsGstEnabledForStore);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        foreach (var result in totals.Lines)
        {
            var line = CartLines.First(l => l.ProductId == result.Line.ProductId);
            line.TaxableAmount = result.TaxableAmount;
            line.GstAmount = result.GstAmount;
            line.LineTotal = result.LineTotal;
        }

        SubTotal = totals.SubTotal;
        ItemDiscountTotal = totals.ItemDiscountTotal;
        BillDiscountAmount = totals.BillDiscountAmount;
        TaxTotal = totals.GstTotal;
        RoundOffAmount = totals.RoundOffAmount;
        GrandTotal = totals.GrandTotal;
    }

    public bool NeedsDiscountAuthorization(decimal percent) =>
        percent > SaleService.MaxUnauthorizedDiscountPercent && DiscountAuthorizedByUserId is null;

    public void SetDiscountAuthorization(int userId) => DiscountAuthorizedByUserId = userId;

    public bool NeedsPriceOverrideAuthorization(CartLineViewModel line) =>
        line.IsPriceOverridden && PriceOverrideAuthorizedByUserId is null;

    public void SetPriceOverrideAuthorization(int userId) => PriceOverrideAuthorizedByUserId = userId;

    public void SetBillDiscount(decimal percent)
    {
        BillDiscountPercent = percent;
        RecalculateCart();
    }

    public async Task<HeldBill> HoldCurrentBillAsync()
    {
        var lines = BuildSaleLineInputs();
        var held = await heldBillService.HoldAsync(lines, BillDiscountPercent, SelectedCustomer?.Id, CashierUserId, note: null);
        ClearCart();
        await RefreshHeldBillsCountAsync();
        return held;
    }

    public Task<IReadOnlyList<HeldBill>> GetHeldBillsAsync() => heldBillService.GetHeldBillsAsync();

    public async Task ResumeHeldBillAsync(int heldBillId)
    {
        var held = await heldBillService.ResumeAsync(heldBillId);
        ClearCart();

        SelectedCustomer = held.Customer;
        BillDiscountPercent = held.BillDiscountPercent;

        foreach (var item in held.Items)
        {
            var product = item.Product;
            CartLines.Add(new CartLineViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Sku = product.Sku,
                Unit = product.Unit.ToString(),
                SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
                OriginalUnitPrice = product.SellingPrice,
                UnitPriceText = product.SellingPrice.ToString("0.##"),
                GstRatePercent = product.GstRatePercent ?? 0,
                IsTaxInclusive = product.IsTaxInclusive,
                QuantityText = item.Quantity.ToString("0.###"),
                DiscountPercentText = item.DiscountPercent.ToString("0.##"),
            });
        }

        RecalculateCart();
        await RefreshHeldBillsCountAsync();
    }

    public void ClearCart()
    {
        CartLines.Clear();
        SelectedCustomer = null;
        BillDiscountPercent = 0;
        DiscountAuthorizedByUserId = null;
        PriceOverrideAuthorizedByUserId = null;
        RecalculateCart();
    }

    public List<SaleLineInput> BuildSaleLineInputs() =>
        CartLines.Select(l => new SaleLineInput
        {
            ProductId = l.ProductId,
            UnitPriceOverride = l.IsPriceOverridden ? l.UnitPrice : null,
            Quantity = l.Quantity,
            DiscountPercent = l.DiscountPercent,
        }).ToList();

    public Task<IReadOnlyList<Customer>> SearchCustomersAsync(string? text) =>
        customerService.SearchAsync(new CustomerSearchQuery { SearchText = text });

    public Task<Customer> CreateCustomerAsync(string name, string? phone, string? address) =>
        customerService.CreateAsync(new CreateCustomerRequest
        {
            Name = name,
            Phone = phone,
            Address = address,
            PerformedByUserId = CashierUserId,
        });
}
