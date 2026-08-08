namespace Kirana.Domain.Entities;

/// <summary>Controls whether a quoted product price already contains GST or GST is added on top.</summary>
public enum PricingType
{
    Inclusive,
    Exclusive,
}
