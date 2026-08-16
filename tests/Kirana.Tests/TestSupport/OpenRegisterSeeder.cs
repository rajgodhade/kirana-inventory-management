using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.TestSupport;

/// <summary>
/// Puts the store in the state a real shop is in while it is trading: register open (Phase 16A-2).
///
/// <para>From 16A-2 a cash sale, cash refund, cash repayment, cash supplier payment or cash expense
/// requires an open register. Most test classes are not testing the register at all — they need a
/// cash transaction as a <i>fixture</i> for something else (reports, printing, pricing, returns) —
/// so they call this once in their constructor and carry on.</para>
///
/// <para>Deliberately NOT folded into <see cref="AuthorizedUserSeeder"/> or the fixture itself:
/// register tests need to control the open/closed state precisely, and a fixture that silently
/// opened a register would quietly invalidate every one of them.</para>
///
/// <para>Writes the row directly rather than through <c>CashRegisterService</c> — this is fixture
/// setup, not a test of opening a register, and going through the service would drag permission
/// checks and audit rows into unrelated tests.</para>
/// </summary>
public static class OpenRegisterSeeder
{
    public static Task<CashRegisterSession> SeedOpenRegisterAsync(
        this SqliteDbContextFixture fixture, int? openedByUserId = null, decimal openingCash = 0m) =>
        fixture.Context.SeedOpenRegisterAsync(openedByUserId, openingCash);

    public static Task<CashRegisterSession> SeedOpenRegisterAsync(
        this SqliteFileDbContextFixture fixture, int? openedByUserId = null, decimal openingCash = 0m) =>
        fixture.Context.SeedOpenRegisterAsync(openedByUserId, openingCash);

    public static async Task<CashRegisterSession> SeedOpenRegisterAsync(
        this KiranaDbContext context, int? openedByUserId = null, decimal openingCash = 0m)
    {
        // Deliberately does NOT invent a Store or User. An earlier attempt did, and it silently
        // broke the classes that run first-time setup themselves: CompleteSetupAsync refuses when a
        // completed Store already exists, and a second Store makes `Stores.SingleAsync()` throw.
        // Callers seed their own owner (which creates the Store) before calling this.
        var storeId = await context.Stores.Select(s => s.Id).FirstOrDefaultAsync();
        if (storeId == 0)
        {
            throw new InvalidOperationException(
                "SeedOpenRegisterAsync needs a Store. Seed the owner (SeedOwnerAsync) first.");
        }

        var userId = openedByUserId ?? await context.Users.Select(u => u.Id).FirstOrDefaultAsync();
        if (userId == 0)
        {
            throw new InvalidOperationException(
                "SeedOpenRegisterAsync needs a User. Seed the owner (SeedOwnerAsync) first.");
        }

        var session = new CashRegisterSession
        {
            StoreId = storeId,
            RegisterName = "Main Register",
            BusinessDate = DateTime.Now.Date,
            Status = CashRegisterStatus.Open,
            OpenedByUserId = userId,
            // Backdated a little so transactions created immediately afterwards land inside the
            // session window rather than racing its opening timestamp.
            OpenedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            OpeningCash = openingCash,
        };

        context.CashRegisterSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    /// <summary>Closes whatever session is open, for tests that need the "register closed" state
    /// after having traded. Writes no Z snapshot — that is <c>CashRegisterService.CloseAsync</c>'s
    /// job and is tested there.</summary>
    public static async Task CloseOpenRegisterAsync(this KiranaDbContext context)
    {
        var open = await context.CashRegisterSessions
            .FirstOrDefaultAsync(s => s.Status == CashRegisterStatus.Open);
        if (open is null) return;

        open.Status = CashRegisterStatus.Closed;
        open.ClosedAtUtc = DateTime.UtcNow;
        open.ClosedByUserId = open.OpenedByUserId;
        await context.SaveChangesAsync();
    }
}
