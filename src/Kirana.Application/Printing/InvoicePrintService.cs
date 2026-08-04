using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Printing;

public sealed class InvoicePrintService(
    IKiranaDbContext db,
    ISaleService saleService,
    IInvoiceDocumentBuilder builder,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer) : IInvoicePrintService
{
    public async Task<InvoiceDocument> GetInvoiceDocumentAsync(int saleId, CancellationToken cancellationToken = default)
    {
        var sale = await saleService.GetByIdAsync(saleId, cancellationToken)
            ?? throw new InvalidOperationException($"Sale #{saleId} was not found.");

        return await BuildAsync(sale, cancellationToken);
    }

    public async Task<InvoiceDocument> GetInvoiceDocumentByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        var sale = await saleService.GetByInvoiceNumberAsync(invoiceNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice '{invoiceNumber}' was not found.");

        return await BuildAsync(sale, cancellationToken);
    }

    public async Task LogPrintAsync(int saleId, int? userId, bool isReprint, CancellationToken cancellationToken = default)
    {
        if (isReprint)
        {
            await permissionEnforcer.EnsureHasPermissionAsync(userId, PermissionKeys.SalesReprintInvoice, cancellationToken);
        }

        await auditLogger.RecordAsync(
            userId, isReprint ? "InvoiceReprinted" : "InvoicePrinted", nameof(Sale), saleId.ToString(),
            cancellationToken: cancellationToken);
    }

    private async Task<InvoiceDocument> BuildAsync(Sale sale, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Store is not configured.");

        return builder.Build(sale, store);
    }
}
