using Kirana.Application.Printing;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Printing;

public class InvoiceDocumentBuilderTests
{
    private readonly InvoiceDocumentBuilder _sut = new();

    private static Store MakeStore(
        string name = "Sharma Kirana Store", string? gstin = "27ABCDE1234F1Z5",
        string? address = "12 MG Road", string? city = "Pune", string? state = "MH", string? pinCode = "411001",
        string? contact = "9876543210", string? footer = "Thank you, visit again!") => new()
    {
        Name = name,
        OwnerName = "Owner",
        Gstin = gstin,
        Address = address,
        City = city,
        State = state,
        PinCode = pinCode,
        ContactNumber = contact,
        InvoiceFooterText = footer,
    };

    private static Sale MakeSale(int id = 1, string invoiceNumber = "INV-2026-000001") => new()
    {
        Id = id,
        InvoiceNumber = invoiceNumber,
        SaleDateUtc = new DateTime(2026, 7, 29, 10, 30, 0, DateTimeKind.Utc),
        SubTotal = 100,
        GrandTotal = 100,
    };

    private static SaleItem MakeItem(
        string name = "Tata Salt 1kg", string code = "PRD-000001", string? sku = "SKU1", string? hsn = "2501",
        decimal qty = 1, decimal unitPrice = 25, decimal gstRate = 5, decimal taxable = 25, decimal gst = 1.25m,
        decimal lineTotal = 26.25m, decimal discountPercent = 0, decimal discountAmount = 0, decimal mrp = 0) => new()
    {
        ProductNameSnapshot = name,
        ProductCodeSnapshot = code,
        SkuSnapshot = sku,
        HsnCodeSnapshot = hsn,
        UnitSnapshot = "Piece",
        GstRatePercentSnapshot = gstRate,
        Quantity = qty,
        UnitPriceSnapshot = unitPrice,
        MrpSnapshot = mrp,
        DiscountPercent = discountPercent,
        DiscountAmount = discountAmount,
        TaxableAmount = taxable,
        GstAmount = gst,
        LineTotal = lineTotal,
    };

    [Fact]
    public void Build_SetsSaleIdAndInvoiceHeader()
    {
        var sale = MakeSale(id: 42, invoiceNumber: "INV-2026-000042");

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(42, document.SaleId);
        Assert.Equal("INV-2026-000042", document.InvoiceNumber);
        Assert.Equal(sale.SaleDateUtc, document.SaleDateUtc);
    }

    [Fact]
    public void Build_UsesHistoricalSnapshotFields_NotLiveProductData()
    {
        var sale = MakeSale();
        sale.Items.Add(MakeItem(name: "Original Name", code: "PRD-000009", unitPrice: 30, lineTotal: 30));
        // SaleItem.Product is intentionally left unset (null) — the builder must never read it.

        var document = _sut.Build(sale, MakeStore());

        var line = Assert.Single(document.Lines);
        Assert.Equal("Original Name", line.ProductName);
        Assert.Equal("PRD-000009", line.ProductCode);
        Assert.Equal(30m, line.UnitPrice);
    }

    [Fact]
    public void Build_GroupsGstByRate_AcrossMultipleLines()
    {
        var sale = MakeSale();
        sale.Items.Add(MakeItem(gstRate: 5, taxable: 100, gst: 5, lineTotal: 105));
        sale.Items.Add(MakeItem(gstRate: 5, taxable: 50, gst: 2.5m, lineTotal: 52.5m));
        sale.Items.Add(MakeItem(gstRate: 12, taxable: 200, gst: 24, lineTotal: 224));

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(2, document.GstGroups.Count);
        var fivePercent = document.GstGroups.Single(g => g.RatePercent == 5);
        Assert.Equal(150m, fivePercent.TaxableAmount);
        Assert.Equal(7.5m, fivePercent.GstAmount);
        var twelvePercent = document.GstGroups.Single(g => g.RatePercent == 12);
        Assert.Equal(200m, twelvePercent.TaxableAmount);
        Assert.Equal(24m, twelvePercent.GstAmount);
    }

    [Fact]
    public void Build_ExcludesZeroRateLines_FromGstGroups()
    {
        var sale = MakeSale();
        sale.Items.Add(MakeItem(gstRate: 0, taxable: 40, gst: 0, lineTotal: 40));

        var document = _sut.Build(sale, MakeStore());

        Assert.Empty(document.GstGroups);
    }

    [Fact]
    public void Build_MapsItemAndBillDiscounts()
    {
        var sale = MakeSale();
        sale.Items.Add(MakeItem(discountPercent: 10, discountAmount: 2.5m, taxable: 22.5m, gst: 1.125m, lineTotal: 23.625m));
        sale.ItemDiscountTotal = 2.5m;
        sale.BillDiscountPercent = 5;
        sale.BillDiscountAmount = 1.18m;

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(2.5m, document.ItemDiscountTotal);
        Assert.Equal(5m, document.BillDiscountPercent);
        Assert.Equal(1.18m, document.BillDiscountAmount);
        var line = Assert.Single(document.Lines);
        Assert.Equal(10m, line.DiscountPercent);
        Assert.Equal(2.5m, line.DiscountAmount);
    }

    [Fact]
    public void Build_MapsMultiplePayments_SplitPayment()
    {
        var sale = MakeSale();
        sale.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = 60, AmountTendered = 100, ChangeGiven = 40 });
        sale.Payments.Add(new Payment { Method = PaymentMethod.Upi, Amount = 40, ReferenceNumber = "UPI-REF-1" });

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(2, document.Payments.Count);
        var cash = document.Payments.Single(p => p.Method == PaymentMethod.Cash);
        Assert.Equal(60m, cash.Amount);
        Assert.Equal(100m, cash.AmountTendered);
        Assert.Equal(40m, cash.ChangeGiven);
        var upi = document.Payments.Single(p => p.Method == PaymentMethod.Upi);
        Assert.Equal(40m, upi.Amount);
        Assert.Equal("UPI-REF-1", upi.ReferenceNumber);
    }

    [Fact]
    public void Build_PopulatesPaymentSummaryLines_FromSalePayments()
    {
        // Confirms InvoiceDocumentBuilder actually wires PaymentSummaryBuilder in — the detailed
        // per-scenario rules themselves are covered by PaymentSummaryBuilderTests.
        var sale = MakeSale();
        sale.GrandTotal = 289m;
        sale.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = 200m });
        sale.Payments.Add(new Payment { Method = PaymentMethod.CustomerCredit, Amount = 89m });

        var document = _sut.Build(sale, MakeStore());

        Assert.Collection(document.PaymentSummaryLines,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(200m, r.Amount); },
            r => { Assert.Equal("Customer Credit", r.Label); Assert.Equal(89m, r.Amount); });
    }

    [Fact]
    public void Build_HandlesMissingCustomer_WithoutThrowing()
    {
        var sale = MakeSale();
        sale.Customer = null;

        var document = _sut.Build(sale, MakeStore());

        Assert.Null(document.CustomerName);
        Assert.Null(document.CustomerPhone);
        Assert.Null(document.CustomerGstin);
    }

    [Fact]
    public void Build_IncludesCustomerDetails_WhenPresent()
    {
        var sale = MakeSale();
        sale.Customer = new Customer { Name = "Regular Customer", Phone = "9998887771", Gstin = "27CUST0001Z9" };

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal("Regular Customer", document.CustomerName);
        Assert.Equal("9998887771", document.CustomerPhone);
        Assert.Equal("27CUST0001Z9", document.CustomerGstin);
    }

    [Fact]
    public void Build_HandlesMissingCashierUser_WithoutThrowing()
    {
        var sale = MakeSale();
        sale.CashierUser = null;

        var document = _sut.Build(sale, MakeStore());

        Assert.Null(document.CashierName);
    }

    [Fact]
    public void Build_IncludesCashierFullName_WhenPresent()
    {
        var sale = MakeSale();
        sale.CashierUser = new User { Username = "cashier1", FullName = "Priya Sharma" };

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal("Priya Sharma", document.CashierName);
    }

    [Fact]
    public void Build_HandlesMissingStoreGstinAndAddress_WithoutThrowing()
    {
        var store = MakeStore(gstin: null, address: null, city: null, state: null, pinCode: null, contact: null);

        var document = _sut.Build(MakeSale(), store);

        Assert.Null(document.StoreGstin);
        Assert.Null(document.StoreAddress);
        Assert.Null(document.StoreContactNumber);
    }

    [Fact]
    public void Build_CombinesStoreAddressParts_SkippingMissingOnes()
    {
        var store = MakeStore(address: "12 MG Road", city: "Pune", state: null, pinCode: "411001");

        var document = _sut.Build(MakeSale(), store);

        Assert.Equal("12 MG Road, Pune, 411001", document.StoreAddress);
    }

    [Fact]
    public void Build_CarriesFooterTextFromStore()
    {
        var store = MakeStore(footer: "No returns after 7 days.");

        var document = _sut.Build(MakeSale(), store);

        Assert.Equal("No returns after 7 days.", document.FooterText);
    }

    [Fact]
    public void Build_ComputesTotalSavings_AsMrpTotalMinusGrandTotal()
    {
        var sale = MakeSale();
        sale.GrandTotal = 158m;
        sale.Items.Add(MakeItem(qty: 1, mrp: 100, lineTotal: 95));
        sale.Items.Add(MakeItem(qty: 2, mrp: 80, lineTotal: 130));
        // MRP total = 1*100 + 2*80 = 260; savings = 260 - 158 = 102.

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(102m, document.TotalSavings);
        Assert.True(document.HasSavings);
    }

    [Fact]
    public void Build_FloorsTotalSavingsAtZero_WhenChargedMoreThanMrpTotal()
    {
        // A price override can legitimately push a line above its own MRP — must never show as a
        // negative "You Saved".
        var sale = MakeSale();
        sale.GrandTotal = 500m;
        sale.Items.Add(MakeItem(qty: 1, mrp: 100, lineTotal: 500));

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(0m, document.TotalSavings);
        Assert.False(document.HasSavings);
    }

    [Fact]
    public void Build_HasNoSavings_ForHistoricalSaleWithNoMrpSnapshot()
    {
        // A sale completed before this field existed defaults MrpSnapshot to 0 — must suppress the
        // "You Saved" row rather than show a meaningless number.
        var sale = MakeSale();
        sale.GrandTotal = 100m;
        sale.Items.Add(MakeItem(qty: 1, mrp: 0, lineTotal: 100));

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(0m, document.TotalSavings);
        Assert.False(document.HasSavings);
    }

    [Fact]
    public void Build_MapsMrpOntoEachLine()
    {
        var sale = MakeSale();
        sale.Items.Add(MakeItem(mrp: 45));

        var document = _sut.Build(sale, MakeStore());

        Assert.Equal(45m, document.Lines.Single().Mrp);
    }
}
