namespace Kirana.Application.Billing;

public sealed class CompleteSaleRequest
{
    public required IReadOnlyList<SaleLineInput> Lines { get; init; }
    public decimal BillDiscountPercent { get; init; }
    public int? CustomerId { get; init; }
    public required IReadOnlyList<SalePaymentInput> Payments { get; init; }
    public int? CashierUserId { get; init; }

    /// <summary>Set only after a successful <c>IAuthenticationService.AuthorizeAsync</c> call for
    /// <see cref="Domain.Entities.PermissionKeys.BillingApproveLargeDiscount"/> (PRD §10). Required
    /// whenever any item or bill discount exceeds <see cref="SaleService.MaxUnauthorizedDiscountPercent"/> —
    /// <see cref="SaleService"/> re-verifies the user actually holds that permission.</summary>
    public int? DiscountAuthorizedByUserId { get; init; }

    /// <summary>Set only after a successful <c>IAuthenticationService.AuthorizeAsync</c> call for
    /// <see cref="Domain.Entities.PermissionKeys.PricingChangeSellingPrice"/>. Required whenever any
    /// line's <see cref="SaleLineInput.UnitPriceOverride"/> differs from that product's current
    /// selling price — <see cref="SaleService"/> re-verifies the user actually holds that
    /// permission.</summary>
    public int? PriceOverrideAuthorizedByUserId { get; init; }
}
