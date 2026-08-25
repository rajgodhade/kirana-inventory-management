using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Kirana.Domain.Taxation;

namespace Kirana.Tests.Setup;

public class FirstTimeSetupServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly FirstTimeSetupService _sut;

    public FirstTimeSetupServiceTests()
    {
        _sut = new FirstTimeSetupService(_fixture.Context, new BCryptPasswordHasher());
    }

    private static CompleteSetupRequest ValidRequest() => new()
    {
        StoreName = "Sharma Kirana Store",
        OwnerName = "Ramesh Sharma",
        AdminUsername = "admin",
        AdminFullName = "Ramesh Sharma",
        AdminPassword = "S3cure!Pass",
        AdminPin = "1234",
    };

    [Fact]
    public async Task IsSetupCompletedAsync_ReturnsFalse_BeforeSetup()
    {
        Assert.False(await _sut.IsSetupCompletedAsync());
    }

    [Fact]
    public async Task CompleteSetupAsync_MarksSetupCompleted()
    {
        await _sut.CompleteSetupAsync(ValidRequest());

        Assert.True(await _sut.IsSetupCompletedAsync());
    }

    [Fact]
    public async Task CompleteSetupAsync_PersistsStoreGstIdentity()
    {
        await _sut.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Hitu Kirana",
            LegalName = "Hitu Kirana Private Limited",
            OwnerName = "Owner",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            GstRegistrationType = GstRegistrationType.Regular,
            AdminUsername = "admin",
            AdminFullName = "Owner",
            AdminPassword = "S3cure!Pass",
        });

        var store = await _fixture.Context.Stores.SingleAsync();
        Assert.Equal("27", store.StateCode);
        Assert.Equal("Maharashtra", store.State);
        Assert.Equal(GstRegistrationType.Regular, store.GstRegistrationType);
    }

    [Fact]
    public async Task CompleteSetupAsync_RejectsGstinStateMismatchBeforeWritingAnything()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Hitu Kirana",
            OwnerName = "Owner",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "29",
            GstRegistrationType = GstRegistrationType.Regular,
            AdminUsername = "admin",
            AdminFullName = "Owner",
            AdminPassword = "S3cure!Pass",
        }));

        Assert.Empty(await _fixture.Context.Stores.ToListAsync());
        Assert.Empty(await _fixture.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task CompleteSetupAsync_SeedsThreeSystemRolesWithPermissions()
    {
        await _sut.CompleteSetupAsync(ValidRequest());

        var roles = await _fixture.Context.Roles
            .Include(r => r.RolePermissions)
            .ToListAsync();

        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, r => r.Name == "Owner" && r.RolePermissions.Count == PermissionKeys.All.Count);
        Assert.Contains(roles, r => r.Name == "Cashier" && r.RolePermissions.Count == PermissionKeys.Cashier.Count);
        Assert.Contains(roles, r => r.Name == "Manager" && r.RolePermissions.Count == PermissionKeys.Manager.Count);
    }

    [Fact]
    public async Task CompleteSetupAsync_CreatesAdminUser_WithHashedPasswordAndPin()
    {
        await _sut.CompleteSetupAsync(ValidRequest());

        var admin = await _fixture.Context.Users.Include(u => u.Role).SingleAsync();

        Assert.Equal("admin", admin.Username);
        Assert.Equal("Owner", admin.Role.Name);
        Assert.NotEqual("S3cure!Pass", admin.PasswordHash);
        Assert.NotNull(admin.PinHash);
        Assert.NotEqual("1234", admin.PinHash);
    }

    [Fact]
    public async Task CompleteSetupAsync_Throws_WhenCalledTwice()
    {
        await _sut.CompleteSetupAsync(ValidRequest());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSetupAsync(ValidRequest()));
    }

    public void Dispose() => _fixture.Dispose();
}
