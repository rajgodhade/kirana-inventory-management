using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Products;
using Kirana.Application.Promotions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Kirana.Application.Taxation;

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
    IPromotionEngine promotionEngine,
    IGstCalculationService gstCalculationService,
    IProductPriceResolver priceResolver,
    IKiranaDbContext db,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _storeName = "Kirana";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerDisplayText))]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(CustomerOutstandingText))]
    [NotifyPropertyChangedFor(nameof(HasCustomerOutstanding))]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private decimal _billDiscountPercent;

    // ==============================  PRICE LEVEL  ==============================
    // The bill's price level lives here, on the active-bill surface, and is snapshotted into
    // BillSessionViewModel when tabs switch - the same place and the same way the customer, bill
    // discount and authorizations already live. One authoritative copy; the tab entries are storage.

    /// <summary>
    /// The level the ACTIVE bill sells at. Retail by default, so a till nobody touches behaves
    /// exactly as it did before this phase existed.
    ///
    /// <para>Changing this does not itself re-price anything — <see cref="ApplyPriceLevelAsync"/>
    /// does, because re-resolving is asynchronous and a property setter cannot await. The selector
    /// calls it; see that method for why the two are separate.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWholesaleSelected))]
    private PriceLevel _selectedPriceLevel = PriceLevel.Retail;

    /// <summary>Drives the selector's visual state without leaking the enum into XAML.</summary>
    public bool IsWholesaleSelected => SelectedPriceLevel == PriceLevel.Wholesale;

    /// <summary>True while any line has no price at the bill's current level. Payment is blocked
    /// until it clears, so a bill labelled Wholesale can never quietly contain a Retail-priced
    /// line.</summary>
    public bool HasUnresolvedLines => CartLines.Any(l => l.HasPriceIssue);

    [ObservableProperty]
    private decimal _subTotal;

    [ObservableProperty]
    private decimal _itemDiscountTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPromotionDiscount))]
    private decimal _promotionDiscountTotal;

    public bool HasPromotionDiscount => PromotionDiscountTotal > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBillDiscount))]
    private decimal _billDiscountAmount;

    [ObservableProperty]
    private decimal _taxTotal;

    [ObservableProperty]
    private decimal _roundOffAmount;

    [ObservableProperty]
    private decimal _grandTotal;

    /// <summary>Total of (MRP − actual price) across the cart, floored at zero — how much the
    /// screen tells the customer they're saving versus buying everything at sticker price.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavings))]
    private decimal _totalSavings;

    /// <summary>Drives hiding the "You Saved" panel entirely rather than showing "₹0.00" when
    /// nothing in the cart has an MRP above its selling price.</summary>
    public bool HasSavings => TotalSavings > 0;

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

    public bool HasSelectedCustomer => SelectedCustomer is not null;

    /// <summary>What this customer already owes, shown next to their name at billing time so the
    /// cashier can ask for repayment before adding more udhaar. Read straight off the customer
    /// record — no new query, no change to how the balance is maintained.</summary>
    public string CustomerOutstandingText =>
        SelectedCustomer is { CreditBalance: var balance } && balance > 0
            ? "₹" + balance.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))
            : string.Empty;

    public bool HasCustomerOutstanding => SelectedCustomer is { CreditBalance: > 0 };

    public bool HasHeldBills => HeldBillsCount > 0;

    public string HeldBillsSummary => HeldBillsCount switch
    {
        0 => "No held bills",
        1 => "1 held bill",
        _ => $"{HeldBillsCount} held bills",
    };

    public int? CashierUserId => session.CurrentUser?.Id;

    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];

    /// <summary>Drives the "cart is empty" placeholder — kept in sync via <see cref="CartLines"/>'s
    /// own change notification rather than recomputed on every keystroke, since it only changes on
    /// add/remove/clear.</summary>
    [ObservableProperty]
    private bool _hasCartLines;

    /// <summary>Number of distinct product lines on the bill. Deliberately a line count rather than
    /// a sum of quantities: quantities carry mixed units (1 Kilogram + 1 Piece), so adding them
    /// together would produce a meaningless "2".</summary>
    [ObservableProperty]
    private int _totalItemCount;

    /// <summary>Whether a bill-level discount is currently applied — drives the inline "remove
    /// discount" affordance, which is hidden when there is nothing to remove.</summary>
    public bool HasBillDiscount => BillDiscountAmount > 0;

    [ObservableProperty]
    private bool _hasSuggestions;

    public ObservableCollection<Product> SearchSuggestions { get; } = [];

    public IScannerInputBuffer ScannerBuffer { get; private set; } = new ScannerInputBuffer();

    /// <summary>Applies persisted keyboard-wedge timing before InitializeAsync wires the scan
    /// callback. Manual product search is deliberately independent of this setting.</summary>
    public void ConfigureScannerTiming(int timeoutMilliseconds)
    {
        if (_scannerWired) return;
        ScannerBuffer = new ScannerInputBuffer(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMilliseconds, 10, 2000)));
    }

    private bool _scannerWired;
    private int _suggestionQueryToken;
    private int _promotionEvaluationToken;
    private bool _applyingPromotionResults;

    // ==============================  BILLING TABS  ==============================
    // Several customers can be part-way through a bill at once. Only the active tab's state lives
    // in the properties above; the rest is snapshotted into its BillSessionViewModel. See that
    // class for why it is done this way rather than by making every binding tab-aware.

    /// <summary>Hard ceiling on simultaneous bills. Ten is already far past what one counter can
    /// track, and an unbounded strip would just become unreadable.</summary>
    public const int MaxBills = 10;

    public ObservableCollection<BillSessionViewModel> Bills { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBillNote))]
    private BillSessionViewModel? _activeBill;

    private int _nextBillId = 1;

    public bool CanAddBill => Bills.Count < MaxBills;

    /// <summary>Closing the last remaining tab would leave nothing to bill into.</summary>
    public bool CanCloseActiveBill => Bills.Count > 1;

    /// <summary>Per-bill note, surfaced as a normal two-way bound field on the active tab.</summary>
    public string ActiveBillNote
    {
        get => ActiveBill?.Note ?? string.Empty;
        set
        {
            if (ActiveBill is not { } bill || bill.Note == value)
            {
                return;
            }

            bill.Note = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Creates the first tab. Called once from <see cref="InitializeAsync"/>.</summary>
    private void EnsureInitialBill()
    {
        if (Bills.Count == 0)
        {
            AddBill();
        }
    }

    /// <summary>Opens a new empty bill and switches to it. No-op at <see cref="MaxBills"/>.</summary>
    public BillSessionViewModel? AddBill()
    {
        if (!CanAddBill)
        {
            ErrorMessage = $"You can have at most {MaxBills} bills open at once.";
            return null;
        }

        var bill = new BillSessionViewModel { Id = _nextBillId, Title = $"Bill {_nextBillId}" };
        _nextBillId++;
        Bills.Add(bill);
        SwitchToBill(bill);
        NotifyTabCommandsChanged();
        return bill;
    }

    /// <summary>
    /// Snapshots the live cart into the outgoing tab and loads the incoming one. Nothing is
    /// recalculated from scratch beyond the usual <see cref="RecalculateCart"/>, so a restored tab
    /// shows exactly the totals it had when the cashier left it.
    /// </summary>
    public void SwitchToBill(BillSessionViewModel bill)
    {
        if (!Bills.Contains(bill) || ReferenceEquals(bill, ActiveBill))
        {
            return;
        }

        CaptureActiveBill();

        ActiveBill = bill;
        foreach (var tab in Bills)
        {
            tab.IsActive = ReferenceEquals(tab, bill);
        }

        // Restore. CartLines is the bound collection, so it is refilled in place rather than
        // replaced — swapping the instance would break the ListView's binding.
        CartLines.Clear();
        foreach (var line in bill.Lines)
        {
            CartLines.Add(line);
        }

        SelectedCustomer = bill.Customer;
        BillDiscountPercent = bill.BillDiscountPercent;
        DiscountAuthorizedByUserId = bill.DiscountAuthorizedByUserId;
        PriceOverrideAuthorizedByUserId = bill.PriceOverrideAuthorizedByUserId;

        // Restored, never re-resolved: the incoming tab's lines already hold the prices they were
        // resolved at, and re-pricing them here would change amounts the cashier had already agreed
        // with a customer just because they glanced at another bill.
        SelectedPriceLevel = bill.PriceLevel;

        OnPropertyChanged(nameof(HasUnresolvedLines));
        OnPropertyChanged(nameof(ActiveBillNote));
        RecalculateCart();
        UpdateActiveBillSummary();
    }

    /// <summary>Copies the live bill state back into whichever tab is currently active.</summary>
    private void CaptureActiveBill()
    {
        if (ActiveBill is not { } current)
        {
            return;
        }

        current.Lines = [.. CartLines];
        current.Customer = SelectedCustomer;
        current.BillDiscountPercent = BillDiscountPercent;
        current.DiscountAuthorizedByUserId = DiscountAuthorizedByUserId;
        current.PriceOverrideAuthorizedByUserId = PriceOverrideAuthorizedByUserId;
        current.PriceLevel = SelectedPriceLevel;
    }

    /// <summary>True when closing this tab would discard goods the cashier already scanned — the
    /// page uses it to decide whether to ask for confirmation first.</summary>
    public bool BillHasItems(BillSessionViewModel bill) =>
        ReferenceEquals(bill, ActiveBill) ? CartLines.Count > 0 : bill.Lines.Count > 0;

    public void CloseBill(BillSessionViewModel bill)
    {
        if (!Bills.Contains(bill) || !CanCloseActiveBill)
        {
            return;
        }

        var wasActive = ReferenceEquals(bill, ActiveBill);
        var index = Bills.IndexOf(bill);
        Bills.Remove(bill);

        if (wasActive)
        {
            // Land on the neighbour the cashier would expect — the tab that shifted into this slot,
            // or the last one if we just closed the rightmost.
            ActiveBill = null;
            SwitchToBill(Bills[Math.Min(index, Bills.Count - 1)]);
        }

        NotifyTabCommandsChanged();
    }

    /// <summary>Keeps the tab chip's subtitle and "has goods" dot in step with the live cart.</summary>
    public void UpdateActiveBillSummary()
    {
        if (ActiveBill is not { } bill)
        {
            return;
        }

        bill.HasItems = CartLines.Count > 0;
        bill.CustomerSummary = SelectedCustomer?.Name ?? "Walk-in";
    }

    private void NotifyTabCommandsChanged()
    {
        OnPropertyChanged(nameof(CanAddBill));
        OnPropertyChanged(nameof(CanCloseActiveBill));
    }

    public async Task InitializeAsync()
    {
        if (!_scannerWired)
        {
            _scannerWired = true;
            ScannerBuffer.BarcodeScanned += barcode => _ = HandleBarcodeScannedAsync(barcode);
            CartLines.CollectionChanged += (_, _) =>
            {
                HasCartLines = CartLines.Count > 0;
                TotalItemCount = CartLines.Count;
                UpdateActiveBillSummary();
            };
        }

        EnsureInitialBill();

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

        await AddOrIncrementAsync(product);
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

        await AddOrIncrementAsync(product);
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

    /// <summary>
    /// Adds a product to the cart at its resolved retail price (Phase 15B-2).
    ///
    /// <para>Async because the price now comes from <see cref="IProductPriceResolver"/> rather than
    /// the <c>Product.SellingPrice</c> column that happened to be loaded with the product. The cart
    /// shows what the till will charge, so it must ask the same source SaleService does — otherwise
    /// the displayed price and the billed price could disagree.</para>
    ///
    /// <para>A product with no resolvable retail price is refused here rather than added at some
    /// guessed figure; SaleService would reject it at checkout anyway, and failing at scan time says
    /// so while the operator can still act on it.</para>
    /// </summary>
    public async Task AddOrIncrementAsync(Product product)
    {
        var existing = CartLines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.QuantityText = (existing.Quantity + 1).ToString("0.###");
            RecalculateCart();
            return;
        }

        // At the bill's CURRENT level, not always retail: a product scanned onto a wholesale bill
        // joins it at wholesale, rather than arriving at retail and waiting to be re-priced.
        var resolution = await priceResolver.ResolveAsync(product.Id, new PricingContext(SelectedPriceLevel));
        if (!resolution.IsResolved)
        {
            ErrorMessage = UnavailableMessage(product.Name, SelectedPriceLevel);
            return;
        }

        var unitPrice = resolution.UnitPrice.Value;

        CartLines.Add(new CartLineViewModel
        {
            ProductId = product.Id,
            ProductName = product.Name,
            ProductCode = product.ProductCode,
            Sku = product.Sku,
            Unit = product.UnitDisplayText ?? product.Unit.ToDisplayText(),
            SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
            OriginalUnitPrice = unitPrice,
            Mrp = product.Mrp,
            UnitPriceText = unitPrice.ToString("0.##"),
            GstRatePercent = product.GstRatePercent ?? 0,
            PricingType = product.PricingType,
            QuantityText = "1",
        });

        RecalculateCart();
    }

    /// <summary>Names the product and the level, because "price unavailable" tells a cashier
    /// nothing they can act on.</summary>
    private static string UnavailableMessage(string productName, PriceLevel level) =>
        $"{level.ToDisplayText()} price is not configured for {productName}.";

    /// <summary>
    /// Switches the bill to <paramref name="level"/> and re-prices every line through the resolver.
    ///
    /// <para>Separate from the <see cref="SelectedPriceLevel"/> setter because re-resolving is
    /// asynchronous — binding a ComboBox straight to an async re-price would leave the selector and
    /// the cart briefly disagreeing, and would re-enter on every restore during a tab switch.</para>
    ///
    /// <para>Lines that cannot be priced at the new level are FLAGGED, not silently left at the old
    /// level's price and not quietly given another level's. They keep their previous amount so the
    /// cashier can see what changed, and payment stays blocked until every flag clears.</para>
    ///
    /// <para>Re-resolving also discards a manual override on the affected line: the override was a
    /// deviation from a price that no longer applies, so carrying the number across would silently
    /// turn an approved retail price into an unapproved wholesale one.</para>
    /// </summary>
    public async Task ApplyPriceLevelAsync(PriceLevel level)
    {
        SelectedPriceLevel = level;
        if (ActiveBill is { } bill)
        {
            bill.PriceLevel = level;
        }

        var unavailable = new List<string>();

        foreach (var line in CartLines)
        {
            var resolution = await priceResolver.ResolveAsync(line.ProductId, new PricingContext(level));
            if (!resolution.IsResolved)
            {
                line.PriceIssue = UnavailableMessage(line.ProductName, level);
                unavailable.Add(line.ProductName);
                continue;
            }

            line.PriceIssue = null;
            line.OriginalUnitPrice = resolution.UnitPrice.Value;
            line.UnitPriceText = resolution.UnitPrice.Value.ToString("0.##");
        }

        ErrorMessage = unavailable.Count == 0
            ? null
            : $"{level.ToDisplayText()} price is not configured for {string.Join(", ", unavailable)}.";

        OnPropertyChanged(nameof(HasUnresolvedLines));
        RecalculateCart();
    }

    public void RemoveLine(CartLineViewModel line)
    {
        CartLines.Remove(line);
        RecalculateCart();
    }

    public void RecalculateCart()
    {
        // Totals now refresh on every keystroke, so a half-typed quantity is a normal, expected
        // state: clearing the box to retype "2" passes through empty (quantity 0) first. Hold the
        // last good totals instead of flashing "quantity must be positive" as the cashier types —
        // OnQuantityLostFocus clamps the value when focus leaves, and SaleService re-validates
        // everything at completion, so nothing invalid can actually be sold.
        if (CartLines.Any(l => l.Quantity <= 0))
        {
            return;
        }

        var cartLines = CartLines.Select(l => new CartLine
        {
            ProductId = l.ProductId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            PricingType = l.PricingType,
            GstRatePercent = l.GstRatePercent,
            DiscountPercent = l.DiscountPercent,
            PromotionBeforeTaxDiscountAmount = l.PromotionBeforeTaxDiscountAmount,
            PromotionAfterTaxDiscountAmount = l.PromotionAfterTaxDiscountAmount,
        }).ToList();

        if (cartLines.Count == 0)
        {
            SubTotal = ItemDiscountTotal = BillDiscountAmount = TaxTotal = RoundOffAmount = GrandTotal = TotalSavings = 0;
            PromotionDiscountTotal = 0;
            return;
        }

        CartTotals totals;
        try
        {
            totals = gstCalculationService.CalculateSales(cartLines, BillDiscountPercent, IsGstEnabledForStore);
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
        PromotionDiscountTotal = totals.PromotionDiscountTotal;
        BillDiscountAmount = totals.BillDiscountAmount;
        TaxTotal = totals.GstTotal;
        RoundOffAmount = totals.RoundOffAmount;
        GrandTotal = totals.GrandTotal;

        // Compared against GrandTotal (what's actually being paid, after every discount/tax), not
        // summed per-line before those — MRP is inherently tax-inclusive, so this is the correct
        // like-for-like figure, mirroring how InvoiceDocumentBuilder computes the same thing for
        // the printed bill.
        var mrpTotal = CartLines.Sum(l => l.Mrp * l.Quantity);
        TotalSavings = Math.Max(0, mrpTotal - GrandTotal);

        if (!_applyingPromotionResults)
        {
            _ = RefreshPromotionsAsync();
        }
    }

    public async Task RefreshPromotionsAsync()
    {
        var token = ++_promotionEvaluationToken;
        if (CartLines.Count == 0 || CartLines.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0))
        {
            return;
        }

        try
        {
            var results = await promotionEngine.EvaluateCartAsync(new PromotionCartContext
            {
                Lines = CartLines.Select(x => new PromotionLineContext
                {
                    ProductId = x.ProductId,
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                }).ToList(),
                BillAmount = CartLines.Sum(x => x.Quantity * x.UnitPrice),
                CustomerId = SelectedCustomer?.Id,
                AtUtc = DateTime.UtcNow,
            });
            if (token != _promotionEvaluationToken)
            {
                return;
            }

            var byProduct = results.ToDictionary(x => x.ProductId);
            foreach (var line in CartLines)
            {
                if (!byProduct.TryGetValue(line.ProductId, out var result))
                {
                    continue;
                }

                line.PromotionBeforeTaxDiscountAmount = result.AppliedPromotions
                    .Where(x => x.CalculationMode == DiscountCalculationMode.BeforeTax).Sum(x => x.DiscountAmount);
                line.PromotionAfterTaxDiscountAmount = result.AppliedPromotions
                    .Where(x => x.CalculationMode == DiscountCalculationMode.AfterTax).Sum(x => x.DiscountAmount);
                line.PromotionDiscountAmount = result.DiscountAmount;
                line.PromotionText = string.Join(" + ", result.AppliedPromotions.Select(x => x.PromotionName));
            }

            _applyingPromotionResults = true;
            RecalculateCart();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Promotions could not be refreshed. {ex.Message}";
        }
        finally
        {
            _applyingPromotionResults = false;
        }
    }

    public bool NeedsDiscountAuthorization(decimal percent) =>
        session.RequirePinForLargeDiscount
        && percent > SaleService.MaxUnauthorizedDiscountPercent && DiscountAuthorizedByUserId is null;

    public void SetDiscountAuthorization(int userId) => DiscountAuthorizedByUserId = userId;

    public bool NeedsPriceOverrideAuthorization(CartLineViewModel line) =>
        session.RequirePinForPriceOverride
        && line.IsPriceOverridden && PriceOverrideAuthorizedByUserId is null;

    public void SetPriceOverrideAuthorization(int userId) => PriceOverrideAuthorizedByUserId = userId;

    public void SetBillDiscount(decimal percent)
    {
        BillDiscountPercent = percent;
        RecalculateCart();
    }

    /// <summary>Removes the bill-level discount without touching item-level ones.</summary>
    public void ClearBillDiscount()
    {
        BillDiscountPercent = 0;

        // Also give back the manager authorization so a later large discount on this same bill has
        // to be approved again — but only when no item line still carries a discount that needs it,
        // otherwise SaleService would refuse the sale at completion for a missing authorization.
        var largestRemainingDiscount = CartLines.Count == 0 ? 0 : CartLines.Max(l => l.DiscountPercent);
        if (largestRemainingDiscount <= SaleService.MaxUnauthorizedDiscountPercent)
        {
            DiscountAuthorizedByUserId = null;
        }

        RecalculateCart();
    }

    public async Task<HeldBill> HoldCurrentBillAsync()
    {
        var lines = BuildSaleLineInputs();

        // The tab's note rides along on HoldAsync's existing note parameter, so a parked bill keeps
        // the cashier's reminder ("waiting for cash") instead of losing it with the tab state.
        var note = string.IsNullOrWhiteSpace(ActiveBillNote) ? null : ActiveBillNote.Trim();

        var held = await heldBillService.HoldAsync(lines, BillDiscountPercent, SelectedCustomer?.Id, CashierUserId, note);
        ClearCart();
        await RefreshHeldBillsCountAsync();
        return held;
    }

    public Task<IReadOnlyList<HeldBill>> GetHeldBillsAsync() => heldBillService.GetHeldBillsAsync();

    public async Task ResumeHeldBillAsync(int heldBillId)
    {
        var held = await heldBillService.ResumeAsync(heldBillId);

        // Resuming must never silently discard whatever is already on the counter. If the current
        // tab is mid-bill, the held bill comes back in its own tab instead — and if all ten are in
        // use we fall back to the old in-place behaviour rather than dropping the resume entirely
        // (the bill is already un-held by this point, so refusing would strand it).
        if (CartLines.Count > 0 && CanAddBill)
        {
            AddBill();
        }

        ClearCart();

        SelectedCustomer = held.Customer;
        BillDiscountPercent = held.BillDiscountPercent;

        foreach (var item in held.Items)
        {
            var product = item.Product;

            // Resumed lines re-resolve too: a bill held yesterday must bill at today's shelf price,
            // which is what happened before when this read the live projection column. Held bills
            // carry no price level of their own, so they resume at the bill's current level, which
            // ClearCart has just reset to Retail.
            var resolution = await priceResolver.ResolveAsync(product.Id, new PricingContext(SelectedPriceLevel));
            if (!resolution.IsResolved)
            {
                ErrorMessage = UnavailableMessage(product.Name, SelectedPriceLevel);
                continue;
            }

            var retailPrice = resolution.UnitPrice.Value;

            CartLines.Add(new CartLineViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Sku = product.Sku,
                Unit = product.UnitDisplayText ?? product.Unit.ToDisplayText(),
                SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
                OriginalUnitPrice = retailPrice,
                Mrp = product.Mrp,
                UnitPriceText = retailPrice.ToString("0.##"),
                GstRatePercent = product.GstRatePercent ?? 0,
                PricingType = product.PricingType,
                QuantityText = item.Quantity.ToString("0.###"),
                DiscountPercentText = item.DiscountPercent.ToString("0.##"),
            });
        }

        if (!string.IsNullOrWhiteSpace(held.Note))
        {
            ActiveBillNote = held.Note;
        }

        RecalculateCart();
        UpdateActiveBillSummary();
        await RefreshHeldBillsCountAsync();
    }

    public void ClearCart()
    {
        CartLines.Clear();
        SelectedCustomer = null;
        BillDiscountPercent = 0;
        DiscountAuthorizedByUserId = null;
        PriceOverrideAuthorizedByUserId = null;

        // Back to Retail for the next customer. A wholesale bill is a deliberate choice per bill,
        // so it must not persist past the sale it was chosen for and quietly discount the next one.
        SelectedPriceLevel = PriceLevel.Retail;

        if (ActiveBill is { } bill)
        {
            bill.Note = string.Empty;
            bill.PriceLevel = PriceLevel.Retail;
            OnPropertyChanged(nameof(ActiveBillNote));
        }

        OnPropertyChanged(nameof(HasUnresolvedLines));

        RecalculateCart();
        UpdateActiveBillSummary();
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
