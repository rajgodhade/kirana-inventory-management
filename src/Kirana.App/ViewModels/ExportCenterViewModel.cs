using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Export;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class ExportCenterViewModel(
    IDataExportService dataExportService, ManagementSession session) : ObservableObject
{
    private static readonly (ExportDataset Dataset, string Title, string Description)[] Catalogue =
    [
        (ExportDataset.Products, "Products", "Full catalogue with pricing, tax and current stock."),
        (ExportDataset.Categories, "Categories", "Category list with the number of products in each."),
        (ExportDataset.Brands, "Brands", "Brand list with the number of products in each."),
        (ExportDataset.Customers, "Customers", "Customer details and outstanding Udhaar balances."),
        (ExportDataset.Suppliers, "Suppliers", "Supplier details and outstanding balances."),
        (ExportDataset.Inventory, "Inventory", "Stock on hand, minimum levels and stock value."),
        (ExportDataset.Sales, "Sales", "Every invoice with totals, tax and payment method."),
        (ExportDataset.Purchases, "Purchases", "Every purchase with supplier, totals and outstanding amount."),
        (ExportDataset.Expenses, "Expenses", "Every expense voucher with category and payment method."),
        (ExportDataset.Promotions, "Promotions", "Offer schedules, scopes, values, status and usage."),
    ];

    public ObservableCollection<ExportDatasetViewModel> Datasets { get; } =
        new(Catalogue
            .Where(entry => session.HasPermission(dataExportService.RequiredPermissionFor(entry.Dataset)))
            .Select(entry => new ExportDatasetViewModel(entry.Dataset, entry.Title, entry.Description)));

    /// <summary>True when the signed-in user can export nothing at all — the page then explains
    /// that rather than showing an empty grid.</summary>
    public bool HasNoDatasets => Datasets.Count == 0;

    public bool CanViewReports => session.HasPermission(PermissionKeys.ReportsView);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;
}

public sealed class ExportDatasetViewModel(ExportDataset dataset, string title, string description)
{
    public ExportDataset Dataset { get; } = dataset;
    public string Title { get; } = title;
    public string Description { get; } = description;
}
