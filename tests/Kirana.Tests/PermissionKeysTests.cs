using Kirana.Domain.Entities;

namespace Kirana.Tests;

public class PermissionKeysTests
{
    [Fact]
    public void SalesReprintInvoice_IsGrantedToOwnerAndManager_ButNotCashier()
    {
        Assert.Contains(PermissionKeys.SalesReprintInvoice, PermissionKeys.Owner);
        Assert.Contains(PermissionKeys.SalesReprintInvoice, PermissionKeys.Manager);
        Assert.DoesNotContain(PermissionKeys.SalesReprintInvoice, PermissionKeys.Cashier);
    }

    [Fact]
    public void SalesReprintInvoice_IsListedInAll()
    {
        Assert.Contains(PermissionKeys.All, p => p.Key == PermissionKeys.SalesReprintInvoice);
    }
}
