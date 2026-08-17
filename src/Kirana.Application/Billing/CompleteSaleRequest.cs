using Kirana.Domain.Entities;

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

    /// <summary>
    /// Which price level this bill is being sold at (Phase 15B-3). Defaults to
    /// <see cref="Domain.Entities.PriceLevel.Retail"/>, so every existing caller — and every bill
    /// that never touches the selector — behaves exactly as it did before.
    ///
    /// <para>This is the level <see cref="SaleService"/> resolves each line at; it is NOT a price.
    /// The client cannot state what a product costs, only which level it is buying at, and the
    /// server establishes the amount from <c>ProductPrice</c>. A product that does not configure
    /// the requested level fails the sale rather than falling back to another level.</para>
    /// </summary>
    public PriceLevel PriceLevel { get; init; } = PriceLevel.Retail;
}
