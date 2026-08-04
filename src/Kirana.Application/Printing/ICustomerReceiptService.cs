namespace Kirana.Application.Printing;

/// <summary>
/// Builds a printable Udhaar repayment receipt and records that it was printed (PRD §31),
/// mirroring <see cref="IInvoicePrintService"/>. Never mutates the payment — printing, reprinting
/// or a failed print has no effect on the underlying financial record.
/// </summary>
public interface ICustomerReceiptService
{
    Task<CustomerReceiptDocument> GetReceiptAsync(int creditPaymentId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task LogPrintAsync(int creditPaymentId, int? userId, CancellationToken cancellationToken = default);
}
