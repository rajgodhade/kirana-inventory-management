namespace Kirana.Application.Customers;

/// <summary>
/// Search by Customer ID, phone, or name (PRD §30). Results are prioritized exact-code &gt;
/// exact-phone &gt; partial name/phone, mirroring the product search's exact-before-partial
/// ordering so a cashier who types a full phone number gets that customer first.
/// </summary>
public sealed class CustomerSearchQuery
{
    public string? SearchText { get; init; }
    public bool IncludeInactive { get; init; }
    public int MaxResults { get; init; } = 50;
}
