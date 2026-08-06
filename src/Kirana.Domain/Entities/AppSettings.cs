using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Single-row application configuration: session locking, backup schedule (PRD §8, §38).
/// </summary>
public class AppSettings : Entity
{
    public int AutoLockMinutes { get; set; } = 10;

    /// <summary>UI theme: "Light" (the default), "Dark", or "System" to follow Windows.</summary>
    public string ThemeMode { get; set; } = "Light";

    public bool AutomaticBackupEnabled { get; set; } = true;
    public string AutomaticBackupFrequency { get; set; } = "Daily";
    public string? BackupDirectory { get; set; }
    public int BackupRetentionCount { get; set; } = 14;

    /// <summary>Whether overriding a product's selling price at the POS needs a manager/owner PIN.
    /// Defaults on; an Owner running the register alone can turn it off in Settings so billing
    /// doesn't stop to ask them to authorize their own action.</summary>
    public bool RequirePinForPriceOverride { get; set; } = true;

    /// <summary>Same idea as <see cref="RequirePinForPriceOverride"/>, for discounts above
    /// <c>SaleService.MaxUnauthorizedDiscountPercent</c>.</summary>
    public bool RequirePinForLargeDiscount { get; set; } = true;

    /// <summary>Same idea, for reprinting a completed invoice from Management Home.</summary>
    public bool RequirePinForReprint { get; set; } = true;

    /// <summary>Preferred file format ("Csv" or "Excel") pre-selected by the Export Center; each
    /// export screen still lets the user pick a different format for a single export.</summary>
    public string DefaultExportFormat { get; set; } = "Csv";
}
