namespace Kirana.Domain.Entities;

/// <summary>Lifecycle state of a finalized purchase (PRD §28). Only <see cref="Completed"/>
/// exists today — extensible for a later returns phase (PRD §9's PurchaseReturn functionality),
/// mirroring <see cref="SaleStatus"/>'s exact same minimalist pattern.</summary>
public enum PurchaseStatus
{
    Completed,
}
