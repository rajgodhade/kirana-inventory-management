using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Inventories;
using Kirana.Application.Printing;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Application.Setup;
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
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IBarcodeService, BarcodeService>();
        services.AddScoped<IBarcodeLookupService, BarcodeLookupService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IHeldBillService, HeldBillService>();
        services.AddScoped<IInvoiceDocumentBuilder, InvoiceDocumentBuilder>();
        services.AddScoped<IInvoicePrintService, InvoicePrintService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        return services;
    }
}
