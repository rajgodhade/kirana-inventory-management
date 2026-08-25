using Kirana.App.ViewModels;
using Kirana.Domain.Entities;

namespace Kirana.App.Tests.Purchases;

public sealed class PurchaseDetailsHistoricalIdentityTests
{
    [Fact]
    public void Historical_purchase_view_uses_supplier_snapshot_instead_of_mutable_master()
    {
        var purchase = new Purchase
        {
            PurchaseNumber = "PUR-2026-000001",
            GstIdentitySnapshotCapturedAtUtc = DateTime.UtcNow,
            SupplierNameSnapshot = "Original Supplier",
            SupplierCodeSnapshot = "SUP-ORIGINAL",
            SupplierGstinSnapshot = "27AAAAA0000A1Z5",
            SupplierStateCodeSnapshot = "27",
            SupplierStateNameSnapshot = "Maharashtra",
            SupplierAddressSnapshot = "Original address",
            Supplier = new Supplier
            {
                Name = "Changed Supplier",
                SupplierCode = "SUP-CHANGED",
                Gstin = "29BBBBB0000B1Z5",
                StateCode = "29",
                Address = "Changed address",
            },
        };

        var view = PurchaseDetailsViewModel.FromPurchase(purchase);

        Assert.Equal("Original Supplier", view.SupplierName);
        Assert.Equal("SUP-ORIGINAL", view.SupplierCode);
        Assert.Equal("27AAAAA0000A1Z5", view.SupplierGstin);
        Assert.Equal("27 · Maharashtra", view.SupplierState);
        Assert.Equal("Original address", view.SupplierAddress);
    }
}
