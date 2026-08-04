using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Billing;

public sealed class HeldBillService(IKiranaDbContext db) : IHeldBillService
{
    public async Task<HeldBill> HoldAsync(
        IReadOnlyList<SaleLineInput> lines, decimal billDiscountPercent, int? customerId, int? cashierUserId,
        string? note, CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            throw new ArgumentException("Cannot hold an empty cart.", nameof(lines));
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var existingCount = await db.Products.CountAsync(p => productIds.Contains(p.Id), cancellationToken);
        if (existingCount != productIds.Count)
        {
            throw new InvalidOperationException("One or more products in the cart could not be found.");
        }

        var heldBill = new HeldBill
        {
            CashierUserId = cashierUserId,
            CustomerId = customerId,
            BillDiscountPercent = billDiscountPercent,
            Note = note,
        };

        foreach (var line in lines)
        {
            heldBill.Items.Add(new HeldBillItem
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                DiscountPercent = line.DiscountPercent,
            });
        }

        db.HeldBills.Add(heldBill);
        await db.SaveChangesAsync(cancellationToken);

        return heldBill;
    }

    public async Task<IReadOnlyList<HeldBill>> GetHeldBillsAsync(CancellationToken cancellationToken = default) =>
        await db.HeldBills
            .Include(h => h.Items).ThenInclude(i => i.Product)
            .Include(h => h.Customer)
            .OrderBy(h => h.HeldAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<HeldBill> ResumeAsync(int heldBillId, CancellationToken cancellationToken = default)
    {
        var heldBill = await db.HeldBills
            .Include(h => h.Items).ThenInclude(i => i.Product)
            .Include(h => h.Customer)
            .FirstOrDefaultAsync(h => h.Id == heldBillId, cancellationToken)
            ?? throw new InvalidOperationException("Held bill was not found.");

        db.HeldBills.Remove(heldBill);
        await db.SaveChangesAsync(cancellationToken);

        return heldBill;
    }
}
