namespace Kirana.Application.Setup;

/// <summary>
/// Everything collected by the first-time setup wizard (PRD §5): store profile,
/// GST configuration, and the admin account to create.
/// </summary>
public sealed class CompleteSetupRequest
{
    public required string StoreName { get; init; }
    public required string OwnerName { get; init; }
    public string? Gstin { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PinCode { get; init; }
    public string? ContactNumber { get; init; }
    public string? AlternateContactNumber { get; init; }
    public string? Email { get; init; }
    public string? LogoPath { get; init; }

    public string InvoicePrefix { get; init; } = "INV";
    public int FinancialYearStartMonth { get; init; } = 4;
    public bool IsGstEnabled { get; init; }
    public string DefaultInvoiceFormat { get; init; } = "80mm";

    public required string AdminUsername { get; init; }
    public required string AdminFullName { get; init; }
    public required string AdminPassword { get; init; }
    public string? AdminPin { get; init; }
}
