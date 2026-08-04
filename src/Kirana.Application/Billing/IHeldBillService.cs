using Kirana.Domain.Entities;

namespace Kirana.Application.Billing;

/// <summary>Hold/Resume for an in-progress cart (PRD §6, §19) — see <see cref="HeldBill"/> for
/// why this isn't modeled as a draft <c>Sale</c>.</summary>
public interface IHeldBillService
{
    Task<HeldBill> HoldAsync(
        IReadOnlyList<SaleLineInput> lines, decimal billDiscountPercent, int? customerId, int? cashierUserId,
        string? note, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HeldBill>> GetHeldBillsAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads the held bill and removes it from the held list — resuming consumes it;
    /// holding the same cart again later creates a fresh entry.</summary>
    Task<HeldBill> ResumeAsync(int heldBillId, CancellationToken cancellationToken = default);
}
