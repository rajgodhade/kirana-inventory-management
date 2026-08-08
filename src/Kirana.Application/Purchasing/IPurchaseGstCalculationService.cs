namespace Kirana.Application.Purchasing;

public interface IPurchaseGstCalculationService
{
    PurchaseTotals Calculate(IReadOnlyList<PurchaseLine> lines);
}
