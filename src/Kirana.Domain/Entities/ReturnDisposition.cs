namespace Kirana.Domain.Entities;

/// <summary>
/// What physically happens to a returned item (PRD §33). This is the difference between stock a
/// shop can sell again and stock it has written off, so it is recorded per returned line rather
/// than per return.
/// </summary>
public enum ReturnDisposition
{
    /// <summary>Goods came back saleable — sellable inventory increases.</summary>
    ReturnToStock,

    /// <summary>Goods came back damaged or expired — sellable inventory does NOT increase, and the
    /// write-off is recorded as a <see cref="StockMovementType.Damaged"/> movement.</summary>
    Damaged,
}
