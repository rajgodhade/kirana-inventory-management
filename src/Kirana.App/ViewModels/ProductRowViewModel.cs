using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Flattened, permission-aware row for the products list (PRD §12-14, §26).</summary>
public sealed class ProductRowViewModel
{
    public required int Id { get; init; }
    public required string ProductCode { get; init; }
    public required string Name { get; init; }
    public string? Sku { get; init; }
    /// <summary>Active barcodes, primary first (Phase 13B) — drives the label dialog's picker.</summary>
    public IReadOnlyList<ProductBarcodeOption> Barcodes { get; init; } = [];

    public string? PrimaryBarcode => Barcodes.FirstOrDefault(b => b.IsPrimary)?.Value ?? Barcodes.FirstOrDefault()?.Value;

    /// <summary>Compact grid text: the primary code, plus "+N more" when there are alternates —
    /// keeps the products row a single line however many codes a product accumulates.</summary>
    public string BarcodeSummary => Barcodes.Count switch
    {
        0 => "",
        1 => PrimaryBarcode!,
        _ => $"{PrimaryBarcode} +{Barcodes.Count - 1} more",
    };
    public string CategoryName { get; init; } = "";
    public string BrandName { get; init; } = "";
    public required string Unit { get; init; }

    public decimal SellingPrice { get; init; }
    public decimal? Mrp { get; init; }
    public decimal? PurchasePrice { get; init; }
    public bool ShowPurchasePrice { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
    public string PricingTypeLabel => PricingType == PricingType.Inclusive ? "GST Included" : "GST Extra";
    public bool IsGstInclusive => PricingType == PricingType.Inclusive;

    public decimal Stock { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Drives the INACTIVE badge beside the product name. Without it a discontinued
    /// product is indistinguishable from a live one whenever "Show inactive" is ticked — the only
    /// other cue was the row menu's Reactivate item, which costs a click per row to discover.</summary>
    public bool IsInactive => !IsActive;

    /// <summary>Dims the whole row for inactive products, so the badge is reinforced by the row's
    /// overall weight rather than being the single thing to spot. Matches the import preview's
    /// treatment of removed rows.</summary>
    public double RowOpacity => IsActive ? 1.0 : 0.55;

    public bool TracksBatches { get; init; }
    public string StockStatus { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Soonest expiry among this product's batches that still have stock (Phase 27), or
    /// null when the product doesn't track batches or none of its batches have an expiry date set.
    /// The nearest date is shown — not every batch's — since that's the one that actually needs
    /// attention first.</summary>
    public DateOnly? NearestExpiryDate { get; init; }

    public string ExpiryDateText => NearestExpiryDate?.ToString("dd-MMM-yyyy") ?? "—";

    /// <summary>Mirrors <see cref="StockStatus"/>'s pattern: a short label for the colored badge
    /// under the date, empty when there's nothing to call out.</summary>
    public string ExpiryStatus => NearestExpiryDate switch
    {
        { } date when date < DateOnly.FromDateTime(DateTime.Today) => "EXPIRED",
        { } date when date <= DateOnly.FromDateTime(DateTime.Today).AddDays(30) => "EXPIRING SOON",
        _ => "",
    };
}
