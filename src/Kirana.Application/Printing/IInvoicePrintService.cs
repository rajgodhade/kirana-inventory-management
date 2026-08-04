using Kirana.Domain.Entities;

namespace Kirana.Application.Printing;

/// <summary>
/// Orchestrates building a printable <see cref="InvoiceDocument"/> for a completed sale and
/// recording that it was printed/reprinted (PRD §23). Never mutates the <see cref="Sale"/> or any
/// stock/payment data — printing (including a failed or retried print) has no effect on the
/// underlying sale record.
/// </summary>
public interface IInvoicePrintService
{
    Task<InvoiceDocument> GetInvoiceDocumentAsync(int saleId, CancellationToken cancellationToken = default);

    Task<InvoiceDocument> GetInvoiceDocumentByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>Audit-logs a print attempt. <paramref name="isReprint"/> distinguishes the
    /// immediate post-sale print ("InvoicePrinted") from a later reprint ("InvoiceReprinted"),
    /// which requires <see cref="Domain.Entities.PermissionKeys.SalesReprintInvoice"/>.</summary>
    Task LogPrintAsync(int saleId, int? userId, bool isReprint, CancellationToken cancellationToken = default);
}
