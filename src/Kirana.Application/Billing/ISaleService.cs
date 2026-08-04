using Kirana.Domain.Entities;

namespace Kirana.Application.Billing;

/// <summary>
/// Completes a POS sale (PRD §19, §43): validates stock/discounts/payments, prices the cart via
/// <see cref="CartPricingCalculator"/>, and atomically persists the Sale, its SaleItems (with a
/// full historical snapshot), Payments, inventory deduction + StockMovements, and any
/// CustomerCredit — all in one transaction, or none of it.
/// </summary>
public interface ISaleService
{
    Task<Sale> CompleteSaleAsync(CompleteSaleRequest request, CancellationToken cancellationToken = default);

    Task<Sale?> GetByIdAsync(int saleId, CancellationToken cancellationToken = default);

    Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default);
}
