namespace Kirana.Domain.Entities;

public enum PromotionType
{
    Percentage,
    FlatAmount,
    FixedSellingPrice,
}

public enum PromotionStatus
{
    Draft,
    Scheduled,
    Running,
    Expired,
    Disabled,
}

public enum PromotionScopeType
{
    EntireStore,
    Category,
    Brand,
    Product,
}

public enum PromotionPriorityMode
{
    HighestDiscount,
    HighestPriority,
}

public enum DiscountCalculationMode
{
    BeforeTax,
    AfterTax,
}
