namespace Kirana.Domain.Entities;

/// <summary>
/// Canonical permission key strings (PRD §9). Seeded once by the Infrastructure seeder;
/// referenced by key (not id) from application code and permission-gate checks.
/// </summary>
public static class PermissionKeys
{
    public const string ProductsView = "products.view";
    public const string ProductsEdit = "products.edit";
    public const string PricingViewPurchasePrice = "pricing.viewPurchasePrice";
    public const string PricingChangeSellingPrice = "pricing.changeSellingPrice";
    public const string BillingApplyDiscount = "billing.applyDiscount";
    public const string BillingApproveLargeDiscount = "billing.approveLargeDiscount";
    public const string SalesProcessRefund = "sales.processRefund";
    public const string SalesCancelInvoice = "sales.cancelInvoice";
    public const string SalesReprintInvoice = "sales.reprintInvoice";
    public const string InventoryManage = "inventory.manage";
    public const string PurchasesManage = "purchases.manage";
    public const string ReportsView = "reports.view";
    public const string ReportsViewProfit = "reports.viewProfit";
    public const string ExpensesManage = "expenses.manage";
    public const string UsersManage = "users.manage";
    public const string SettingsChange = "settings.change";
    public const string BackupRestore = "backup.restore";
    public const string AuditLogView = "auditlog.view";

    public static readonly IReadOnlyList<(string Key, string Description)> All = new[]
    {
        (ProductsView, "View products"),
        (ProductsEdit, "Create/edit products"),
        (PricingViewPurchasePrice, "View purchase price and margins"),
        (PricingChangeSellingPrice, "Change selling price"),
        (BillingApplyDiscount, "Apply discounts at POS"),
        (BillingApproveLargeDiscount, "Authorize discounts above the cashier's normal limit"),
        (SalesProcessRefund, "Process refunds"),
        (SalesCancelInvoice, "Cancel a completed invoice"),
        (SalesReprintInvoice, "Reprint a completed invoice"),
        (InventoryManage, "Manage inventory and stock adjustments"),
        (PurchasesManage, "Manage purchases and suppliers"),
        (ReportsView, "View reports and dashboard"),
        (ReportsViewProfit, "View profit figures"),
        (ExpensesManage, "Manage expenses"),
        (UsersManage, "Manage users and roles"),
        (SettingsChange, "Change application settings"),
        (BackupRestore, "Perform backup and restore"),
        (AuditLogView, "View audit logs"),
    };

    public static readonly IReadOnlyList<string> Owner = All.Select(p => p.Key).ToArray();

    public static readonly IReadOnlyList<string> Cashier = new[]
    {
        ProductsView,
        BillingApplyDiscount,
    };

    public static readonly IReadOnlyList<string> Manager = new[]
    {
        ProductsView, ProductsEdit,
        BillingApplyDiscount, BillingApproveLargeDiscount, SalesProcessRefund, SalesCancelInvoice, SalesReprintInvoice,
        InventoryManage, PurchasesManage,
        ReportsView,
        ExpensesManage,
    };
}
