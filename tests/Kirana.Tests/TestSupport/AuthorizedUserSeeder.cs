using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.TestSupport;

/// <summary>Seeds the standard Owner/Manager/Cashier roles + an Owner-permission admin user via
/// the real <see cref="FirstTimeSetupService"/> seeding path, for tests of services that now
/// require a permitted <c>performedByUserId</c>. Shared across test classes so every test doesn't
/// re-implement the same setup boilerplate.</summary>
public static class AuthorizedUserSeeder
{
    public static async Task<User> SeedOwnerAsync(this SqliteDbContextFixture fixture)
    {
        var setup = new FirstTimeSetupService(fixture.Context, new BCryptPasswordHasher());
        await setup.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Test Store",
            OwnerName = "Test Owner",
            AdminUsername = "owner",
            AdminFullName = "Test Owner",
            AdminPassword = "Owner@12345",
            AdminPin = "1234",
        });

        return await fixture.Context.Users.Include(u => u.Role).SingleAsync();
    }

    public static async Task<User> SeedCashierAsync(this SqliteDbContextFixture fixture)
    {
        var cashierRole = await fixture.Context.Roles.SingleAsync(r => r.Name == "Cashier");
        var cashier = new User
        {
            Username = $"cashier-{Guid.NewGuid():N}"[..15],
            FullName = "Test Cashier",
            PasswordHash = new BCryptPasswordHasher().Hash("Cashier@123"),
            Role = cashierRole,
            IsActive = true,
        };
        fixture.Context.Users.Add(cashier);
        await fixture.Context.SaveChangesAsync();
        return cashier;
    }
}
