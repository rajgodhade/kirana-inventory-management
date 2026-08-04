using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Customers;

public sealed class CustomerService(IKiranaDbContext db, IAuditLogger auditLogger) : ICustomerService
{
    public async Task<Customer> CreateAsync(string name, string? phone, string? address, string? gstin, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Customer name is required.", nameof(name));
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (normalizedPhone is not null && await db.Customers.AnyAsync(c => c.Phone == normalizedPhone, cancellationToken))
        {
            throw new InvalidOperationException($"A customer with phone '{normalizedPhone}' already exists.");
        }

        var customer = new Customer
        {
            Name = name.Trim(),
            Phone = normalizedPhone,
            Address = address,
            Gstin = gstin,
            IsActive = true,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "CustomerCreated", nameof(Customer), customer.Id.ToString(),
            newValue: customer.Name, cancellationToken: cancellationToken);

        return customer;
    }

    public async Task<IReadOnlyList<Customer>> SearchAsync(string? searchText, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        var query = db.Customers.Where(c => c.IsActive);

        var text = searchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            query = query.Where(c => EF.Functions.Like(c.Name, like) || (c.Phone != null && EF.Functions.Like(c.Phone, like)));
        }

        return await query.OrderBy(c => c.Name).Take(maxResults).ToListAsync(cancellationToken);
    }

    public Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
}
