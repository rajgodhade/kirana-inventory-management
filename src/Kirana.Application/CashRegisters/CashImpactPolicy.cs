using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.CashRegisters;

/// <summary>
/// Decides whether a transaction moves physical money, and therefore whether it needs an open cash
/// register (Phase 16A-2).
///
/// <para>One place, deliberately. The alternative — an "is the register open?" check written into
/// each of the five services that can move cash — is how the definition of "cash-impacting" drifts:
/// one path forgets split payments, another forgets that <see cref="RefundMethod"/> is a different
/// enum from <see cref="PaymentMethod"/>, and the drawer silently stops reconciling. Everything that
/// can empty the till answers the same question here.</para>
///
/// <para>The predicates are pure and take no database, so they are testable on their own; only
/// <see cref="EnsureOpenRegisterAsync"/> touches storage. That split matters because the interesting
/// rule (what counts as cash) is the part worth testing exhaustively.</para>
///
/// <para><b>Why block rather than absorb.</b> A cash transaction recorded with no open session
/// belongs to no session at all, so nothing ever reconciles it — see
/// <c>docs/CashRegister.md</c>. Assigning it to the next session would be worse: that session's
/// opening cash was physically counted without this money in the drawer.</para>
/// </summary>
public static class CashImpactPolicy
{
    /// <summary>The message every blocked path returns. Tells the operator what to do rather than
    /// what failed, and never leaks a database or session detail.</summary>
    public const string NoOpenRegisterMessage =
        "No cash register is open. Open the register before processing cash transactions.";

    /// <summary>
    /// True when any tender on the bill is cash.
    ///
    /// <para><b>Any</b>, not "the" — a sale carries a list of payments and split tenders are already
    /// supported (a Cash + Udhaar bill exists in this repository's own data). A rule written against
    /// a single "the payment method" would wave through exactly the bills that put cash in the
    /// drawer alongside something else.</para>
    /// </summary>
    public static bool RequiresOpenRegister(IEnumerable<PaymentMethod> paymentMethods) =>
        paymentMethods.Any(method => method == PaymentMethod.Cash);

    /// <summary>Single-tender overload for the flows that take one method — supplier payments,
    /// customer repayments and expenses.</summary>
    public static bool RequiresOpenRegister(PaymentMethod paymentMethod) =>
        paymentMethod == PaymentMethod.Cash;

    /// <summary>
    /// Refunds answer the same question through a different enum.
    ///
    /// <para><see cref="RefundMethod.StoreCredit"/> and <see cref="RefundMethod.None"/> move no
    /// physical money — the first adjusts a ledger, the second is an exchange — so neither needs a
    /// register. Keeping this overload next to the others is the point: the two enums are easy to
    /// confuse, and a refund path that reached for <see cref="PaymentMethod"/> would not compile.</para>
    /// </summary>
    public static bool RequiresOpenRegister(RefundMethod refundMethod) =>
        refundMethod == RefundMethod.Cash;

    /// <summary>
    /// Throws unless a register is currently open. Call this <b>before</b> any financial mutation,
    /// so a rejected transaction leaves nothing behind.
    ///
    /// <para>Reads with <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/> so
    /// the answer is the committed state of the database rather than whatever this context happened
    /// to load earlier. A register closed by another context — the manager on a second screen — must
    /// be seen immediately; the Phase 13C stale-identity-map bug was exactly this shape.</para>
    /// </summary>
    public static async Task EnsureOpenRegisterAsync(IKiranaDbContext db, CancellationToken cancellationToken)
    {
        var isOpen = await db.CashRegisterSessions
            .AsNoTracking()
            .AnyAsync(session => session.Status == CashRegisterStatus.Open, cancellationToken);

        if (!isOpen)
        {
            throw new InvalidOperationException(NoOpenRegisterMessage);
        }
    }

    /// <summary>Convenience for the single-method flows: check the rule and enforce it together.</summary>
    public static Task EnsureRegisterAvailableForAsync(
        IKiranaDbContext db, PaymentMethod paymentMethod, CancellationToken cancellationToken) =>
        RequiresOpenRegister(paymentMethod) ? EnsureOpenRegisterAsync(db, cancellationToken) : Task.CompletedTask;

    /// <inheritdoc cref="EnsureRegisterAvailableForAsync(IKiranaDbContext, PaymentMethod, CancellationToken)"/>
    public static Task EnsureRegisterAvailableForAsync(
        IKiranaDbContext db, RefundMethod refundMethod, CancellationToken cancellationToken) =>
        RequiresOpenRegister(refundMethod) ? EnsureOpenRegisterAsync(db, cancellationToken) : Task.CompletedTask;

    /// <inheritdoc cref="EnsureRegisterAvailableForAsync(IKiranaDbContext, PaymentMethod, CancellationToken)"/>
    public static Task EnsureRegisterAvailableForAsync(
        IKiranaDbContext db, IEnumerable<PaymentMethod> paymentMethods, CancellationToken cancellationToken) =>
        RequiresOpenRegister(paymentMethods) ? EnsureOpenRegisterAsync(db, cancellationToken) : Task.CompletedTask;
}
