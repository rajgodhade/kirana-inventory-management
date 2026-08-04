using Kirana.Application.Abstractions;
using Kirana.Application.Expenses;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Printing;

/// <summary>
/// Builds the Phase 9 printable slips (PRD §32-33), mirroring <see cref="ICustomerReceiptService"/>:
/// documents are assembled from stored records, and printing never mutates the underlying
/// financial record — a failed or cancelled print is always safe to retry.
/// </summary>
public interface IReturnReceiptService
{
    Task<ReturnReceiptDocument> GetReturnReceiptAsync(int salesReturnId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<ExpenseReceiptDocument> GetExpenseReceiptAsync(int expenseId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task LogReturnPrintAsync(int salesReturnId, int? userId, CancellationToken cancellationToken = default);

    Task LogExpensePrintAsync(int expenseId, int? userId, CancellationToken cancellationToken = default);
}

public sealed class ReturnReceiptService(
    IKiranaDbContext db,
    ISalesReturnService salesReturnService,
    IExpenseService expenseService,
    IAuditLogger auditLogger) : IReturnReceiptService
{
    public async Task<ReturnReceiptDocument> GetReturnReceiptAsync(
        int salesReturnId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        // Permission is enforced inside the return service, so there is exactly one place that
        // decides who may see refund data.
        var salesReturn = await salesReturnService.GetByIdAsync(salesReturnId, performedByUserId, cancellationToken)
            ?? throw new InvalidOperationException($"Return #{salesReturnId} was not found.");

        var store = await GetStoreAsync(cancellationToken);
        var processedBy = await GetUserNameAsync(salesReturn.ProcessedByUserId, cancellationToken);

        return new ReturnReceiptDocument
        {
            SalesReturnId = salesReturn.Id,
            StoreName = store.Name,
            StoreAddress = BuildStoreAddress(store),
            StoreContactNumber = store.ContactNumber,
            FooterText = store.InvoiceFooterText,
            ReturnNumber = salesReturn.ReturnNumber,
            InvoiceNumber = salesReturn.InvoiceNumberSnapshot,
            ReturnDateUtc = salesReturn.ReturnDateUtc,
            ProcessedByName = processedBy,
            CustomerName = salesReturn.Customer?.Name,
            CustomerCode = salesReturn.Customer?.CustomerCode,
            CustomerPhone = salesReturn.Customer?.Phone,
            TotalReturnAmount = salesReturn.TotalReturnAmount,
            RefundAmount = salesReturn.RefundAmount,
            RefundMethod = salesReturn.RefundMethod.ToString(),
            ReferenceNumber = salesReturn.ReferenceNumber,
            Reason = salesReturn.Reason,
            Notes = salesReturn.Notes,
            Lines = salesReturn.Items.Select(i => new ReturnReceiptLine
            {
                ProductName = i.ProductNameSnapshot,
                ProductCode = i.ProductCodeSnapshot,
                Unit = i.UnitSnapshot,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPriceSnapshot,
                LineRefundAmount = i.LineRefundAmount,
                Disposition = i.Disposition == ReturnDisposition.Damaged ? "Damaged" : "Returned to stock",
            }).ToList(),
        };
    }

    public async Task<ExpenseReceiptDocument> GetExpenseReceiptAsync(
        int expenseId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var expense = await expenseService.GetByIdAsync(expenseId, performedByUserId, cancellationToken)
            ?? throw new InvalidOperationException($"Expense #{expenseId} was not found.");

        var store = await GetStoreAsync(cancellationToken);

        return new ExpenseReceiptDocument
        {
            ExpenseId = expense.Id,
            StoreName = store.Name,
            StoreAddress = BuildStoreAddress(store),
            StoreContactNumber = store.ContactNumber,
            FooterText = store.InvoiceFooterText,
            ExpenseNumber = expense.ExpenseNumber,
            ExpenseDateUtc = expense.ExpenseDateUtc,
            // The snapshot, not the live category, so a reprint years later matches the original.
            CategoryName = expense.CategoryNameSnapshot,
            Amount = expense.Amount,
            PaymentMethod = expense.PaymentMethod.ToString(),
            ReferenceNumber = expense.ReferenceNumber,
            Description = expense.Description,
            Notes = expense.Notes,
            RecordedByName = expense.CreatedByUser?.FullName,
        };
    }

    public Task LogReturnPrintAsync(int salesReturnId, int? userId, CancellationToken cancellationToken = default) =>
        auditLogger.RecordAsync(
            userId, "ReturnReceiptPrinted", nameof(SalesReturn), salesReturnId.ToString(),
            cancellationToken: cancellationToken);

    public Task LogExpensePrintAsync(int expenseId, int? userId, CancellationToken cancellationToken = default) =>
        auditLogger.RecordAsync(
            userId, "ExpenseReceiptPrinted", nameof(Expense), expenseId.ToString(),
            cancellationToken: cancellationToken);

    private async Task<Store> GetStoreAsync(CancellationToken cancellationToken) =>
        await db.Stores.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Store is not configured.");

    private async Task<string?> GetUserNameAsync(int? userId, CancellationToken cancellationToken) =>
        userId is { } id
            ? (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken))?.FullName
            : null;

    private static string? BuildStoreAddress(Store store)
    {
        var parts = new[] { store.Address, store.City, store.State, store.PinCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var combined = string.Join(", ", parts);
        return combined.Length == 0 ? null : combined;
    }
}
