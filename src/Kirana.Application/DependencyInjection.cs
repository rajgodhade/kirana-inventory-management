using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.CashRegisters;
using Kirana.Application.Export;
using Kirana.Application.Expenses;
using Kirana.Application.Inventories;
using Kirana.Application.Hardware;
using Kirana.Application.Printing;
using Kirana.Application.Promotions;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Kirana.Application.Returns;
using Kirana.Application.Setup;
using Kirana.Application.Taxation;
using Kirana.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Kirana.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ManagementSession>();
        services.AddScoped<IFirstTimeSetupService, FirstTimeSetupService>();
        services.AddScoped<IPermissionSeedingService, PermissionSeedingService>();
        services.AddScoped<IPermissionEnforcer, PermissionEnforcer>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductImportService, ProductImportService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IBarcodeService, BarcodeService>();
        services.AddScoped<IBarcodeLookupService, BarcodeLookupService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddSingleton<IGstCalculationService, GstCalculationService>();
        services.AddSingleton<IPurchaseGstCalculationService, PurchaseGstCalculationService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IPromotionEngine, PromotionEngine>();
        services.AddScoped<IHeldBillService, HeldBillService>();
        services.AddScoped<IInvoiceDocumentBuilder, InvoiceDocumentBuilder>();
        services.AddScoped<IInvoicePrintService, InvoicePrintService>();
        services.AddScoped<ICustomerReceiptService, CustomerReceiptService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISalesReturnService, SalesReturnService>();
        services.AddScoped<IPurchaseReturnService, PurchaseReturnService>();
        services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IReturnReceiptService, ReturnReceiptService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<IProductReportService, ProductReportService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<IExpenseReportService, ExpenseReportService>();
        services.AddScoped<IProfitReportService, ProfitReportService>();
        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IAutomaticBackupScheduler, AutomaticBackupScheduler>();
        services.AddScoped<IHardwareSettingsService, HardwareSettingsService>();
        services.AddScoped<IReceiptHardwareGuard, ReceiptHardwareGuard>();
        return services;
    }
}
