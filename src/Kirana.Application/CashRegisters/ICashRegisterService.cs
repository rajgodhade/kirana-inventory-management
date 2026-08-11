using Kirana.Domain.Entities;

namespace Kirana.Application.CashRegisters;

public interface ICashRegisterService
{
    Task<CashRegisterStatusSummary> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<CashRegisterReport> GetCurrentReportAsync(int performedByUserId, decimal? countedCashPreview = null, CancellationToken cancellationToken = default);
    Task<CashRegisterSession> OpenAsync(OpenRegisterRequest request, CancellationToken cancellationToken = default);
    Task<CashMovement> RecordMovementAsync(RecordCashMovementRequest request, CancellationToken cancellationToken = default);
    Task<CashRegisterReport> GetXReportAsync(int performedByUserId, decimal? countedCashPreview = null, CancellationToken cancellationToken = default);
    Task<CashRegisterReport> CloseAsync(CloseRegisterRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashRegisterHistoryRow>> GetHistoryAsync(int performedByUserId, int take = 100, CancellationToken cancellationToken = default);
    Task<CashRegisterReport> GetZReportAsync(int sessionId, int performedByUserId, CancellationToken cancellationToken = default);
}
