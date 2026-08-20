using Kirana.Domain.Common;
using Kirana.Domain.Taxation;

namespace Kirana.Domain.Entities;

/// <summary>
/// Single-row store profile captured during first-time setup (PRD §5).
/// </summary>
public class Store : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? StateCode { get; set; }
    public GstRegistrationType? GstRegistrationType { get; set; }
    public string? PinCode { get; set; }
    public string? ContactNumber { get; set; }
    public string? AlternateContactNumber { get; set; }
    public string? Email { get; set; }
    public string? LogoPath { get; set; }

    public string InvoicePrefix { get; set; } = "INV";
    public int FinancialYearStartMonth { get; set; } = 4;
    public bool IsGstEnabled { get; set; }
    public string DefaultInvoiceFormat { get; set; } = "80mm";
    public string? DefaultPrinterName { get; set; }
    public string? InvoiceFooterText { get; set; }

    public bool SetupCompleted { get; set; }
}
