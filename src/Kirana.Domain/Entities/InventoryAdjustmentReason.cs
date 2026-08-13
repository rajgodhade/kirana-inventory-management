namespace Kirana.Domain.Entities;

/// <summary>
/// Why stock was corrected by hand (Phase 13D). A controlled list rather than free text, because
/// "how much stock do we lose to damage vs theft" is only answerable if the reason is a value you
/// can group by — notes stay free-form for the human detail.
/// </summary>
public enum InventoryAdjustmentReason
{
    /// <summary>Goods broken or spoiled in handling.</summary>
    Damaged,

    /// <summary>Past their expiry date and pulled from the shelf.</summary>
    Expired,

    /// <summary>Missing with no known cause — distinct from
    /// <see cref="TheftOrShrinkage"/>, which is a deliberate accusation.</summary>
    Lost,

    /// <summary>Believed taken. Kept separate from <see cref="Lost"/> so shrinkage reporting is
    /// not diluted by ordinary misplacement.</summary>
    TheftOrShrinkage,

    /// <summary>Stock discovered that the system did not know about.</summary>
    Found,

    /// <summary>A previous entry was wrong — including compensating a mistaken adjustment. This is
    /// the reason to use when undoing an earlier correction, since adjustments are immutable.</summary>
    DataCorrection,

    /// <summary>Setting or fixing the starting quantity for a product.</summary>
    OpeningBalance,

    /// <summary>Anything else. Requires notes, so it can never become an unexplained catch-all.</summary>
    Other,
}

/// <summary>
/// Which way a manual adjustment moves stock (Phase 13D). Callers pass a direction plus a positive
/// magnitude rather than a signed number: a bare "-5" is easy to mis-key as "5" and silently do the
/// opposite of what was meant, and the sign is exactly the part a tired operator gets wrong.
/// </summary>
public enum InventoryAdjustmentDirection
{
    Increase,
    Decrease,
}

public static class InventoryAdjustmentExtensions
{
    /// <summary>Notes carry the only human explanation an auditor will ever see, so
    /// <see cref="InventoryAdjustmentReason.Other"/> — which says nothing on its own — cannot be
    /// used without them.</summary>
    public static bool RequiresNotes(this InventoryAdjustmentReason reason) =>
        reason == InventoryAdjustmentReason.Other;

    /// <summary>Reads as "+5" / "-5" for display, so direction survives without relying on colour.</summary>
    public static string ToSignPrefix(this InventoryAdjustmentDirection direction) =>
        direction == InventoryAdjustmentDirection.Increase ? "+" : "-";

    /// <summary>Applies the direction to a positive magnitude. The single place the sign is
    /// decided, so no caller can invent its own convention.</summary>
    public static decimal ToSignedQuantity(this InventoryAdjustmentDirection direction, decimal magnitude) =>
        direction == InventoryAdjustmentDirection.Increase ? magnitude : -magnitude;

    public static string ToDisplayText(this InventoryAdjustmentReason reason) => reason switch
    {
        InventoryAdjustmentReason.TheftOrShrinkage => "Theft / shrinkage",
        InventoryAdjustmentReason.DataCorrection => "Data correction",
        InventoryAdjustmentReason.OpeningBalance => "Opening balance",
        _ => reason.ToString(),
    };
}
