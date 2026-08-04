namespace Kirana.Application.Purchasing;

/// <summary>Search by Supplier ID or name (PRD §28).</summary>
public sealed class SupplierSearchQuery
{
    public string? SearchText { get; init; }
    public bool IncludeInactive { get; init; }
    public int MaxResults { get; init; } = 200;
}
