using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public enum CashMovementType
{
    CashIn,
    CashOut,
}

/// <summary>A manual physical drawer movement. Transactional cash is derived from its source table.</summary>
public sealed class CashMovement : Entity
{
    public int RegisterSessionId { get; set; }
    public CashRegisterSession RegisterSession { get; set; } = null!;
    public Guid OperationId { get; set; }
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int PerformedByUserId { get; set; }
    public User PerformedByUser { get; set; } = null!;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
