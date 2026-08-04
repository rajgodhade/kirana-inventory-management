using Kirana.Domain.Entities;

namespace Kirana.Application.Printing;

/// <summary>
/// Builds a printable <see cref="InvoiceDocument"/> from a completed <see cref="Sale"/> (with its
/// Items/Payments/Customer/CashierUser loaded) and the current <see cref="Store"/> settings. Pure
/// and synchronous — no I/O — so it is trivially unit-testable and safe to call as many times as
/// needed for preview/print/reprint without any side effects.
/// </summary>
public interface IInvoiceDocumentBuilder
{
    InvoiceDocument Build(Sale sale, Store store);
}
