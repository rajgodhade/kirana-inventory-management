using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Customers;

public sealed class CustomerService(IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger)
    : ICustomerService
{
    private const string CustomerSequenceKey = "Customer";
    private const string CustomerCodePrefix = "CUST";
    private const int CustomerCodePadding = 6;

    public async Task<Customer> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var name = RequireName(request.Name);
        var phone = Normalize(request.Phone);
        await EnsurePhoneAvailableAsync(phone, excludingCustomerId: null, cancellationToken);
        GstinValidator.EnsureValidForWrite(request.Gstin, request.StateCode);

        var customerCode = await sequenceGenerator.NextAsync(
            CustomerSequenceKey, CustomerCodePrefix, CustomerCodePadding, cancellationToken);

        var customer = new Customer
        {
            CustomerCode = customerCode,
            Name = name,
            Phone = phone,
            Address = Normalize(request.Address),
            Gstin = Normalize(request.Gstin),
            StateCode = Normalize(request.StateCode),
            GstRegistrationType = request.GstRegistrationType,
            Notes = Normalize(request.Notes),
            DefaultPriceLevel = request.DefaultPriceLevel,
            IsActive = true,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(request.PerformedByUserId, "CustomerCreated", nameof(Customer), customer.Id.ToString(),
            newValue: $"{customer.CustomerCode} - {customer.Name}", cancellationToken: cancellationToken);

        return customer;
    }

    public async Task<Customer> UpdateAsync(int customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        var phone = Normalize(request.Phone);
        await EnsurePhoneAvailableAsync(phone, customerId, cancellationToken);
        GstinValidator.EnsureValidForWrite(request.Gstin, request.StateCode);

        var previousGstIdentity = DescribeGstIdentity(customer.Gstin, customer.StateCode, customer.GstRegistrationType);

        customer.Name = RequireName(request.Name);
        customer.Phone = phone;
        customer.Address = Normalize(request.Address);
        customer.Gstin = Normalize(request.Gstin);
        customer.StateCode = Normalize(request.StateCode);
        customer.GstRegistrationType = request.GstRegistrationType;
        customer.Notes = Normalize(request.Notes);

        // Captured before the assignment so the audit can say what the preference actually moved
        // from — the existing entry only records identity, which would leave a pricing-relevant
        // change invisible in the trail.
        var previousPriceLevel = customer.DefaultPriceLevel;
        customer.DefaultPriceLevel = request.DefaultPriceLevel;

        customer.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(request.PerformedByUserId, "CustomerUpdated", nameof(Customer), customer.Id.ToString(),
            previousValue: previousPriceLevel == request.DefaultPriceLevel
                ? null
                : $"Default price level: {Describe(previousPriceLevel)}",
            newValue: previousPriceLevel == request.DefaultPriceLevel
                ? $"{customer.CustomerCode} - {customer.Name}"
                : $"{customer.CustomerCode} - {customer.Name}; default price level: {Describe(customer.DefaultPriceLevel)}",
            cancellationToken: cancellationToken);

        var newGstIdentity = DescribeGstIdentity(customer.Gstin, customer.StateCode, customer.GstRegistrationType);
        if (!string.Equals(previousGstIdentity, newGstIdentity, StringComparison.Ordinal))
        {
            await auditLogger.RecordAsync(request.PerformedByUserId, "CustomerGstIdentityUpdated", nameof(Customer),
                customer.Id.ToString(), previousGstIdentity, newGstIdentity, cancellationToken: cancellationToken);
        }

        return customer;
    }

    public async Task<Customer> SetActiveAsync(int customerId, bool isActive, int? performedByUserId = null, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        if (customer.IsActive == isActive)
        {
            return customer;
        }

        // Deactivating someone who still owes money would hide that debt from the active-customer
        // views while leaving the balance on the books.
        if (!isActive && customer.CreditBalance > 0)
        {
            throw new InvalidOperationException(
                $"'{customer.Name}' still owes ₹{customer.CreditBalance:0.00}. Settle the outstanding Udhaar before deactivating.");
        }

        customer.IsActive = isActive;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, isActive ? "CustomerReactivated" : "CustomerDeactivated",
            nameof(Customer), customer.Id.ToString(), cancellationToken: cancellationToken);

        return customer;
    }

    public Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

    public async Task<IReadOnlyList<Customer>> SearchAsync(CustomerSearchQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> Filtered() =>
            query.IncludeInactive ? db.Customers : db.Customers.Where(c => c.IsActive);

        var text = query.SearchText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return await Filtered().OrderBy(c => c.Name).Take(query.MaxResults).ToListAsync(cancellationToken);
        }

        var results = new List<Customer>();
        var seenIds = new HashSet<int>();

        void AddRange(IEnumerable<Customer> customers)
        {
            foreach (var customer in customers)
            {
                if (seenIds.Add(customer.Id))
                {
                    results.Add(customer);
                }
            }
        }

        // 1. Exact Customer ID or phone — the two things a cashier types in full.
        AddRange(await Filtered().Where(c => c.CustomerCode == text || c.Phone == text).ToListAsync(cancellationToken));

        // 2. Partial name / phone.
        if (results.Count < query.MaxResults)
        {
            var like = $"%{text}%";
            AddRange(await Filtered()
                .Where(c => EF.Functions.Like(c.Name, like) || (c.Phone != null && EF.Functions.Like(c.Phone, like)))
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken));
        }

        return results.Take(query.MaxResults).ToList();
    }

    private async Task EnsurePhoneAvailableAsync(string? phone, int? excludingCustomerId, CancellationToken cancellationToken)
    {
        if (phone is null)
        {
            return;
        }

        var taken = await db.Customers.AnyAsync(
            c => c.Phone == phone && c.Id != (excludingCustomerId ?? 0), cancellationToken);
        if (taken)
        {
            throw new InvalidOperationException($"A customer with phone '{phone}' already exists.");
        }
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Customer name is required.");
        }

        return name.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Renders the optional preference for the audit trail. "No preference" is a real,
    /// distinct state and reads better than an empty value.</summary>
    private static string Describe(PriceLevel? level) => level?.ToDisplayText() ?? "No preference";

    private static string DescribeGstIdentity(string? gstin, string? stateCode, GstRegistrationType? registrationType) =>
        $"GSTIN: {gstin ?? "Not set"}; state code: {stateCode ?? "Not set"}; registration: {registrationType?.ToString() ?? "Not specified"}";
}
