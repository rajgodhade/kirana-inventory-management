using Kirana.Application.Billing;

namespace Kirana.Application.Taxation;

public interface IGstCalculationService
{
    CartTotals CalculateSales(IReadOnlyList<CartLine> lines, decimal billDiscountPercent, bool isGstEnabledForStore);
    IReadOnlyList<GstSlabSummary> SummarizeStored(IReadOnlyList<GstSnapshotLine> lines);
    bool ValidateStored(IReadOnlyList<GstSnapshotLine> lines, GstStoredTotals totals, string context);
}
