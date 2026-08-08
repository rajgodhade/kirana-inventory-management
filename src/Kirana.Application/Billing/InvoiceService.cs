using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Billing;

/// <summary>Read projection over immutable completed sales. Managers/owners can see all sales;
/// cashiers are limited to their own completed invoices.</summary>
public sealed class InvoiceService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IInvoiceService
{
    public async Task<bool> CanAccessAsync(int saleId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (performedByUserId is null) return false;
        var canViewAll = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.SalesReprintInvoice, cancellationToken);
        return canViewAll
            ? await db.Sales.AnyAsync(s => s.Id == saleId, cancellationToken)
            : await db.Sales.AnyAsync(s => s.Id == saleId && s.CashierUserId == performedByUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceListItem>> SearchAsync(
        InvoiceSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (performedByUserId is null)
        {
            throw new UnauthorizedAccessException("Sign in to view invoices.");
        }

        // Reprint permission is already the established policy boundary for access to the broader
        // invoice archive. A cashier may still review the sales they completed themselves.
        var canViewAll = await permissionEnforcer.HasPermissionAsync(
            performedByUserId, PermissionKeys.SalesReprintInvoice, cancellationToken);

        var sales = db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.CashierUser)
            .Include(s => s.Items).ThenInclude(i => i.Promotions)
            .Include(s => s.Payments)
            .AsQueryable();

        if (!canViewAll)
        {
            sales = sales.Where(s => s.CashierUserId == performedByUserId);
        }

        if (query.FromUtc is { } fromUtc) sales = sales.Where(s => s.SaleDateUtc >= fromUtc);
        if (query.ToUtc is { } toUtc) sales = sales.Where(s => s.SaleDateUtc <= toUtc);
        if (query.CashierId is { } cashierId) sales = sales.Where(s => s.CashierUserId == cashierId);
        if (query.CustomerId is { } customerId) sales = sales.Where(s => s.CustomerId == customerId);
        if (query.Status is { } status) sales = sales.Where(s => s.Status == status);
        if (query.PaymentMethod is { } method) sales = sales.Where(s => s.Payments.Any(p => p.Method == method));
        if (query.HasPromotion is { } hasPromotion)
        {
            sales = hasPromotion
                ? sales.Where(s => s.Items.Any(i => i.Promotions.Any()))
                : sales.Where(s => !s.Items.Any(i => i.Promotions.Any()));
        }

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var like = $"%{text}%";
            sales = sales.Where(s => s.InvoiceNumber == text
                || EF.Functions.Like(s.InvoiceNumber, like)
                || (s.Customer != null && (EF.Functions.Like(s.Customer.Name, like)
                    || (s.Customer.Phone != null && EF.Functions.Like(s.Customer.Phone, like)))));
        }

        sales = query.SortBy switch
        {
            InvoiceSortBy.Oldest => sales.OrderBy(s => s.SaleDateUtc),
            InvoiceSortBy.AmountHighToLow => sales.OrderByDescending(s => s.GrandTotal).ThenByDescending(s => s.SaleDateUtc),
            InvoiceSortBy.AmountLowToHigh => sales.OrderBy(s => s.GrandTotal).ThenByDescending(s => s.SaleDateUtc),
            _ => sales.OrderByDescending(s => s.SaleDateUtc),
        };

        var records = await sales.Take(query.MaxResults).ToListAsync(cancellationToken);
        return records.Select(s => new InvoiceListItem
        {
            SaleId = s.Id,
            InvoiceNumber = s.InvoiceNumber,
            CustomerName = s.Customer?.Name ?? "Walk-in Customer",
            CustomerId = s.CustomerId,
            CustomerPhone = s.Customer?.Phone,
            CashierName = s.CashierUser?.FullName ?? "System",
            CashierUserId = s.CashierUserId,
            SaleDateUtc = s.SaleDateUtc,
            TotalItems = s.Items.Sum(i => i.Quantity),
            PaymentMethodText = s.Payments.Count == 0 ? "Payment not recorded" : string.Join(" + ", s.Payments.Select(p => p.Method).Distinct()),
            PromotionText = s.Items.SelectMany(i => i.Promotions).Select(p => p.PromotionCodeSnapshot).FirstOrDefault(),
            GrandTotal = s.GrandTotal,
            Status = s.Status,
        }).ToList();
    }
}
