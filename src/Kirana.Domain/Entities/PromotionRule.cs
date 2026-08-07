using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>Extension point for coupon, membership, loyalty, Buy-X-Get-Y and combo rules. The
/// first engine version uses the strongly typed core fields on Promotion; future rule handlers can
/// add rows without changing the promotion table.</summary>
public class PromotionRule : Entity
{
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public string RuleType { get; set; } = string.Empty;
    public string? RuleValue { get; set; }
    public int Sequence { get; set; }
    public bool IsActive { get; set; } = true;
}
