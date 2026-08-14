using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.Tests.TestSupport;

/// <summary>
/// Builds a <see cref="PurchaseOrderEntryViewModel"/> over in-memory stand-ins for the application
/// services. The ViewModel holds no XAML types, so its popup and selection rules can be exercised
/// without a UI thread; only the service boundary needs faking.
/// </summary>
public sealed class PurchaseOrderEntryFixture
{
    public FakeProductService Products { get; } = new();
    public FakeSupplierService Suppliers { get; } = new();

    public PurchaseOrderEntryViewModel CreateViewModel() => new(
        Products, Suppliers, new UnusedPurchaseOrderService(), new PassThroughGstCalculator(), new ManagementSession());

    public Product AddProduct(string name, string code, decimal purchasePrice = 10m, string? sku = null, bool isActive = true)
    {
        var product = new Product
        {
            Id = Products.Items.Count + 1,
            Name = name,
            ProductCode = code,
            Sku = sku,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = purchasePrice,
            Mrp = purchasePrice + 5,
            SellingPrice = purchasePrice + 3,
            IsActive = isActive,
            PricingType = PricingType.Exclusive,
        };
        Products.Items.Add(product);
        return product;
    }

    public Supplier AddSupplier(string name, string code, string phone = "9876543210", bool isActive = true)
    {
        var supplier = new Supplier
        {
            Id = Suppliers.Items.Count + 1,
            Name = name,
            SupplierCode = code,
            Phone = phone,
            IsActive = isActive,
        };
        Suppliers.Items.Add(supplier);
        return supplier;
    }
}

public sealed class FakeProductService : IProductService
{
    public List<Product> Items { get; } = [];

    /// <summary>Mirrors the real service's name / SKU / code / barcode matching closely enough to
    /// prove the picker keeps using one lookup path.</summary>
    public Task<IReadOnlyList<Product>> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken = default)
    {
        var term = query.SearchText?.Trim() ?? string.Empty;
        IEnumerable<Product> matches = Items;
        if (!string.IsNullOrEmpty(term))
        {
            matches = matches.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (p.Sku ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                // Retired barcodes stay unmatchable, mirroring the real lookup.
                || p.Barcodes.Any(b => b.IsActive && b.NormalizedValue.Equals(term, StringComparison.OrdinalIgnoreCase)));
        }
        return Task.FromResult<IReadOnlyList<Product>>(matches.Take(query.MaxResults).ToList());
    }

    public Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == productId));

    public Task<Product> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<Product> UpdateAsync(int productId, UpdateProductRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task SetActiveAsync(int productId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed class FakeSupplierService : ISupplierService
{
    public List<Supplier> Items { get; } = [];

    public Task<IReadOnlyList<Supplier>> SearchAsync(SupplierSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Supplier>>(Items.ToList());

    public Task<Supplier?> GetByIdAsync(int supplierId, int? performedByUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Id == supplierId));

    public Task<Supplier> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<Supplier> UpdateAsync(int supplierId, UpdateSupplierRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task SetActiveAsync(int supplierId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<SupplierOverview>> SearchOverviewAsync(SupplierSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<SupplierLedgerEntry>> GetLedgerAsync(int supplierId, int? performedByUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Saving is out of scope for picker/popup tests; every call would be a test bug.</summary>
public sealed class UnusedPurchaseOrderService : IPurchaseOrderService
{
    public Task<PurchaseOrder> CreateDraftAsync(SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<PurchaseOrder> UpdateDraftAsync(int purchaseOrderId, SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<PurchaseOrder> SubmitAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<PurchaseOrder> CancelAsync(CancelPurchaseOrderRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<PurchaseOrder?> GetByIdAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PurchaseOrder?>(null);
    public Task<IReadOnlyList<PurchaseOrder>> SearchAsync(PurchaseOrderSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PurchaseOrder>>([]);
}

/// <summary>Totals are covered by the real GST tests; here they only need to not throw.</summary>
public sealed class PassThroughGstCalculator : IPurchaseGstCalculationService
{
    public PurchaseTotals Calculate(IReadOnlyList<PurchaseLine> lines)
    {
        var results = lines.Select(line =>
        {
            var lineTotal = line.Quantity * line.UnitPrice;
            return new PurchaseLineResult
            {
                Line = line,
                GrossAmount = lineTotal,
                TaxableAmount = lineTotal,
                LineTotal = lineTotal,
            };
        }).ToList();

        var subTotal = results.Sum(r => r.LineTotal);
        return new PurchaseTotals
        {
            Lines = results,
            SubTotal = subTotal,
            TaxableTotal = subTotal,
            GrandTotal = subTotal,
        };
    }
}
