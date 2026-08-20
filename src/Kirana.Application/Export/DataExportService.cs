using System.Globalization;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Export;

public sealed class DataExportService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IDataExportService
{
    public string RequiredPermissionFor(ExportDataset dataset) => dataset switch
    {
        ExportDataset.Products or ExportDataset.Categories or ExportDataset.Brands => PermissionKeys.ProductsView,
        ExportDataset.Customers => PermissionKeys.CustomersManage,
        ExportDataset.Suppliers or ExportDataset.Purchases => PermissionKeys.PurchasesManage,
        ExportDataset.Inventory => PermissionKeys.InventoryManage,
        ExportDataset.Sales => PermissionKeys.ReportsView,
        ExportDataset.Expenses => PermissionKeys.ExpensesManage,
        ExportDataset.Promotions => PermissionKeys.PromotionsView,
        _ => throw new ArgumentOutOfRangeException(nameof(dataset)),
    };

    public string DisplayNameFor(ExportDataset dataset) => dataset.ToString();

    public async Task<ReportExportData> BuildExportAsync(
        ExportDataset dataset,
        int? performedByUserId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, RequiredPermissionFor(dataset), cancellationToken);

        return dataset switch
        {
            ExportDataset.Products => await BuildProductsAsync(performedByUserId, cancellationToken),
            ExportDataset.Categories => await BuildCategoriesAsync(cancellationToken),
            ExportDataset.Brands => await BuildBrandsAsync(cancellationToken),
            ExportDataset.Customers => await BuildCustomersAsync(cancellationToken),
            ExportDataset.Suppliers => await BuildSuppliersAsync(cancellationToken),
            ExportDataset.Inventory => await BuildInventoryAsync(performedByUserId, cancellationToken),
            ExportDataset.Sales => await BuildSalesAsync(fromUtc, toUtc, cancellationToken),
            ExportDataset.Purchases => await BuildPurchasesAsync(fromUtc, toUtc, cancellationToken),
            ExportDataset.Expenses => await BuildExpensesAsync(fromUtc, toUtc, cancellationToken),
            ExportDataset.Promotions => await BuildPromotionsAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(dataset)),
        };
    }

    private async Task<ReportExportData> BuildPromotionsAsync(CancellationToken cancellationToken)
    {
        var promotions = await db.Promotions.AsNoTracking().Include(x => x.Schedule)
            .Include(x => x.Scope).ThenInclude(x => x!.Targets).OrderBy(x => x.PromotionName).ToListAsync(cancellationToken);
        return new ReportExportData
        {
            Title = "Promotions",
            Subtitle = $"{promotions.Count} promotion(s) - exported {Now()}",
            Columns = ["Code", "Name", "Type", "Scope", "Value", "Start UTC", "End UTC", "Status", "Priority", "Stacking", "Usage", "Active"],
            Rows = promotions.Select(x => (IReadOnlyList<string>)
            [
                x.PromotionCode, x.PromotionName, x.PromotionType.ToString(), x.Scope?.ScopeType.ToString() ?? string.Empty,
                x.PromotionType == PromotionType.Percentage ? NumberOrBlank(x.Percentage) : x.PromotionType == PromotionType.FlatAmount ? NumberOrBlank(x.FlatAmount) : NumberOrBlank(x.FixedPrice),
                x.Schedule?.StartAtUtc.ToString("O") ?? string.Empty, x.Schedule?.EndAtUtc.ToString("O") ?? string.Empty,
                x.Status.ToString(), x.Priority.ToString(CultureInfo.InvariantCulture), Bool(x.AllowStacking),
                x.MaximumUsage is { } maximum ? $"{x.CurrentUsage}/{maximum}" : x.CurrentUsage.ToString(CultureInfo.InvariantCulture), Bool(x.IsActive),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildProductsAsync(int? performedByUserId, CancellationToken cancellationToken)
    {
        // Purchase price is management-only data (PRD §14) — a user who may export the catalogue
        // but not see cost gets the same column list with the cost cells blank, rather than a
        // silently different file shape that would break a downstream template.
        var canViewCost = await permissionEnforcer.HasPermissionAsync(
            performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        var products = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.ProductCode,
                p.Name,
                p.Sku,
                // Projected as a list and joined in memory below — EF can't translate string.Join.
                // Primary first so a single-barcode export reads exactly as it did before Phase 13B.
                Barcodes = p.Barcodes.Where(b => b.IsActive)
                    .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id)
                    .Select(b => b.Value).ToList(),
                CategoryName = p.Category != null ? p.Category.Name : null,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                p.Unit,
                p.PurchasePrice,
                p.Mrp,
                p.SellingPrice,
                p.WholesalePrice,
                p.GstRatePercent,
                p.HsnCode,
                p.PricingType,
                p.MinimumStock,
                p.ReorderQuantity,
                p.IsActive,
                QuantityOnHand = p.Inventory != null ? p.Inventory.QuantityOnHand : 0m,
            })
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Products",
            Subtitle = $"{products.Count} product(s) — exported {Now()}",
            Columns =
            [
                "Product Code", "Name", "SKU", "Barcodes", "Category", "Brand", "Unit",
                "Purchase Price", "MRP", "Selling Price", "Wholesale Price", "GST %", "HSN Code",
                "Pricing Type", "Stock On Hand", "Minimum Stock", "Reorder Quantity", "Active",
            ],
            Rows = products.Select(p => (IReadOnlyList<string>)
            [
                // Pipe-separated to match the import format, so an export round-trips back through
                // ProductImportService unchanged.
                p.ProductCode, p.Name, p.Sku ?? string.Empty, string.Join("|", p.Barcodes),
                CategoryName(p.CategoryName), BrandName(p.BrandName), p.Unit.ToString(),
                canViewCost ? Number(p.PurchasePrice) : string.Empty,
                Number(p.Mrp), Number(p.SellingPrice), NumberOrBlank(p.WholesalePrice),
                NumberOrBlank(p.GstRatePercent), p.HsnCode ?? string.Empty, p.PricingType.ToString(),
                Number(p.QuantityOnHand), Number(p.MinimumStock), Number(p.ReorderQuantity), Bool(p.IsActive),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Categories.AsNoTracking()
            .Select(c => new { c.Name, c.IsActive, ProductCount = c.Products.Count })
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Categories",
            Subtitle = $"{rows.Count} categor(ies) — exported {Now()}",
            Columns = ["Name", "Products", "Active"],
            Rows = rows.Select(c => (IReadOnlyList<string>)
                [c.Name, c.ProductCount.ToString(CultureInfo.InvariantCulture), Bool(c.IsActive)]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildBrandsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Brands.AsNoTracking()
            .Select(b => new { b.Name, b.IsActive, ProductCount = b.Products.Count })
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Brands",
            Subtitle = $"{rows.Count} brand(s) — exported {Now()}",
            Columns = ["Name", "Products", "Active"],
            Rows = rows.Select(b => (IReadOnlyList<string>)
                [b.Name, b.ProductCount.ToString(CultureInfo.InvariantCulture), Bool(b.IsActive)]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildCustomersAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Customers.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Customers",
            Subtitle = $"{rows.Count} customer(s) — exported {Now()}",
            Columns = ["Customer Code", "Name", "Phone", "Address", "GSTIN", "Outstanding Credit", "Notes", "Active"],
            Rows = rows.Select(c => (IReadOnlyList<string>)
            [
                c.CustomerCode, c.Name, c.Phone ?? string.Empty, c.Address ?? string.Empty,
                c.Gstin ?? string.Empty, Number(c.CreditBalance), c.Notes ?? string.Empty, Bool(c.IsActive),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildSuppliersAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Suppliers",
            Subtitle = $"{rows.Count} supplier(s) — exported {Now()}",
            Columns = ["Supplier Code", "Name", "Contact Person", "Phone", "Email", "GSTIN", "Address", "Outstanding Balance", "Active"],
            Rows = rows.Select(s => (IReadOnlyList<string>)
            [
                s.SupplierCode, s.Name, s.ContactPerson ?? string.Empty, s.Phone ?? string.Empty,
                s.Email ?? string.Empty, s.Gstin ?? string.Empty, s.Address ?? string.Empty,
                Number(s.OutstandingBalance), Bool(s.IsActive),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildInventoryAsync(int? performedByUserId, CancellationToken cancellationToken)
    {
        var canViewCost = await permissionEnforcer.HasPermissionAsync(
            performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        var rows = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.ProductCode,
                p.Name,
                CategoryName = p.Category != null ? p.Category.Name : null,
                p.Unit,
                p.PurchasePrice,
                p.MinimumStock,
                p.IsActive,
                QuantityOnHand = p.Inventory != null ? p.Inventory.QuantityOnHand : 0m,
            })
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Inventory",
            Subtitle = $"{rows.Count} product(s) — stock as of {Now()}",
            Columns = ["Product Code", "Name", "Category", "Unit", "Stock On Hand", "Minimum Stock", "Stock Value", "Below Minimum", "Active"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
            [
                r.ProductCode, r.Name, CategoryName(r.CategoryName), r.Unit.ToString(),
                Number(r.QuantityOnHand), Number(r.MinimumStock),
                canViewCost ? Number(r.QuantityOnHand * r.PurchasePrice) : string.Empty,
                Bool(r.QuantityOnHand < r.MinimumStock), Bool(r.IsActive),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildSalesAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
    {
        var query = db.Sales.AsNoTracking();
        if (fromUtc is { } from)
        {
            query = query.Where(s => s.SaleDateUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(s => s.SaleDateUtc <= to);
        }

        var rows = await query
            .Select(s => new
            {
                s.InvoiceNumber,
                s.SaleDateUtc,
                CustomerName = s.GstIdentitySnapshotCapturedAtUtc != null
                    ? s.CustomerNameSnapshot
                    : s.Customer != null ? s.Customer.Name : null,
                CashierName = s.CashierUser != null ? s.CashierUser.FullName : null,
                ItemCount = s.Items.Count,
                s.SubTotal,
                s.ItemDiscountTotal,
                s.BillDiscountAmount,
                s.TaxTotal,
                s.RoundOffAmount,
                s.GrandTotal,
                s.Status,
                s.PriceLevel,
                PaymentMethods = s.Payments.Select(p => p.Method).ToList(),
            })
            .OrderByDescending(s => s.SaleDateUtc)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Sales",
            Subtitle = BuildRangeSubtitle(rows.Count, "sale(s)", fromUtc, toUtc),
            Columns =
            [
                "Invoice Number", "Date", "Customer", "Cashier", "Items", "Subtotal",
                "Item Discount", "Bill Discount", "GST", "Round Off", "Grand Total", "Payment Method(s)", "Status",
                // Appended rather than inserted: anything consuming this export by column position
                // keeps working, and a new trailing column is the safest way to extend it.
                "Price Level",
            ],
            Rows = rows.Select(s => (IReadOnlyList<string>)
            [
                s.InvoiceNumber, LocalTimestamp(s.SaleDateUtc), s.CustomerName ?? "Walk-in",
                s.CashierName ?? string.Empty, s.ItemCount.ToString(CultureInfo.InvariantCulture),
                Number(s.SubTotal), Number(s.ItemDiscountTotal), Number(s.BillDiscountAmount),
                Number(s.TaxTotal), Number(s.RoundOffAmount), Number(s.GrandTotal),
                string.Join(" + ", s.PaymentMethods.Select(ReportFormatting.FormatPaymentMethod)),
                s.Status.ToString(),
                s.PriceLevel.ToDisplayText(),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildPurchasesAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
    {
        var query = db.Purchases.AsNoTracking();
        if (fromUtc is { } from)
        {
            query = query.Where(p => p.PurchaseDateUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(p => p.PurchaseDateUtc <= to);
        }

        var rows = await query
            .Select(p => new
            {
                p.PurchaseNumber,
                p.PurchaseDateUtc,
                SupplierName = p.GstIdentitySnapshotCapturedAtUtc != null
                    ? p.SupplierNameSnapshot
                    : p.Supplier.Name,
                p.SupplierInvoiceNumber,
                ItemCount = p.Items.Count,
                p.SubTotal,
                p.DiscountTotal,
                p.TaxTotal,
                p.GrandTotal,
                p.AmountPaid,
                p.OutstandingAmount,
                p.Status,
            })
            .OrderByDescending(p => p.PurchaseDateUtc)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Purchases",
            Subtitle = BuildRangeSubtitle(rows.Count, "purchase(s)", fromUtc, toUtc),
            Columns =
            [
                "Purchase Number", "Date", "Supplier", "Supplier Invoice", "Items", "Subtotal",
                "Discount", "GST", "Grand Total", "Amount Paid", "Outstanding", "Status",
            ],
            Rows = rows.Select(p => (IReadOnlyList<string>)
            [
                p.PurchaseNumber, LocalTimestamp(p.PurchaseDateUtc), p.SupplierName ?? "Supplier",
                p.SupplierInvoiceNumber ?? string.Empty, p.ItemCount.ToString(CultureInfo.InvariantCulture),
                Number(p.SubTotal), Number(p.DiscountTotal), Number(p.TaxTotal), Number(p.GrandTotal),
                Number(p.AmountPaid), Number(p.OutstandingAmount), p.Status.ToString(),
            ]).ToList(),
        };
    }

    private async Task<ReportExportData> BuildExpensesAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
    {
        var query = db.Expenses.AsNoTracking();
        if (fromUtc is { } from)
        {
            query = query.Where(e => e.ExpenseDateUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(e => e.ExpenseDateUtc <= to);
        }

        var rows = await query
            .Select(e => new
            {
                e.ExpenseNumber,
                e.ExpenseDateUtc,
                e.CategoryNameSnapshot,
                e.Amount,
                e.PaymentMethod,
                e.Description,
                e.ReferenceNumber,
                CreatedBy = e.CreatedByUser != null ? e.CreatedByUser.FullName : null,
            })
            .OrderByDescending(e => e.ExpenseDateUtc)
            .ToListAsync(cancellationToken);

        return new ReportExportData
        {
            Title = "Expenses",
            Subtitle = BuildRangeSubtitle(rows.Count, "expense(s)", fromUtc, toUtc),
            Columns = ["Expense Number", "Date", "Category", "Amount", "Payment Method", "Description", "Reference", "Recorded By"],
            Rows = rows.Select(e => (IReadOnlyList<string>)
            [
                e.ExpenseNumber, LocalTimestamp(e.ExpenseDateUtc), e.CategoryNameSnapshot,
                Number(e.Amount), ReportFormatting.FormatPaymentMethod(e.PaymentMethod),
                e.Description ?? string.Empty, e.ReferenceNumber ?? string.Empty, e.CreatedBy ?? string.Empty,
            ]).ToList(),
        };
    }

    private static string BuildRangeSubtitle(int count, string noun, DateTime? fromUtc, DateTime? toUtc)
    {
        var range = (fromUtc, toUtc) switch
        {
            (null, null) => "all time",
            ({ } f, null) => $"from {LocalDate(f)}",
            (null, { } t) => $"up to {LocalDate(t)}",
            ({ } f, { } t) => $"{LocalDate(f)} to {LocalDate(t)}",
        };

        return $"{count} {noun}, {range} — exported {Now()}";
    }

    private static string CategoryName(string? name) => name ?? "Uncategorized";

    private static string BrandName(string? name) => name ?? string.Empty;

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string NumberOrBlank(decimal? value) => value is { } v ? Number(v) : string.Empty;

    private static string Bool(bool value) => value ? "Yes" : "No";

    private static string LocalTimestamp(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string LocalDate(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
