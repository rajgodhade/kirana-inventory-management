using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.TestSupport;

/// <summary>Seeds the standard Owner/Manager/Cashier roles + an Owner-permission admin user via
/// the real <see cref="FirstTimeSetupService"/> seeding path, for tests of services that now
/// require a permitted <c>performedByUserId</c>. Shared across test classes so every test doesn't
/// re-implement the same setup boilerplate.</summary>
public static class AuthorizedUserSeeder
{
    public static Task<User> SeedOwnerAsync(this SqliteDbContextFixture fixture) =>
        fixture.Context.SeedOwnerAsync();

    public static Task<User> SeedCashierAsync(this SqliteDbContextFixture fixture) =>
        fixture.Context.SeedCashierAsync();

    public static Task<User> SeedOwnerAsync(this SqliteFileDbContextFixture fixture) =>
        fixture.Context.SeedOwnerAsync();

    public static Task<User> SeedCashierAsync(this SqliteFileDbContextFixture fixture) =>
        fixture.Context.SeedCashierAsync();

    public static async Task<User> SeedOwnerAsync(this KiranaDbContext context)
    {
        var setup = new FirstTimeSetupService(context, new BCryptPasswordHasher());
        await setup.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Test Store",
            OwnerName = "Test Owner",
            AdminUsername = "owner",
            AdminFullName = "Test Owner",
            AdminPassword = "Owner@12345",
            AdminPin = "1234",
        });

        return await context.Users.Include(u => u.Role).SingleAsync();
    }

    public static async Task<User> SeedCashierAsync(this KiranaDbContext context)
    {
        var cashierRole = await context.Roles.SingleAsync(r => r.Name == "Cashier");
        var cashier = new User
        {
            Username = $"cashier-{Guid.NewGuid():N}"[..15],
            FullName = "Test Cashier",
            PasswordHash = new BCryptPasswordHasher().Hash("Cashier@123"),
            Role = cashierRole,
            IsActive = true,
        };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();
        return cashier;
    }
}
