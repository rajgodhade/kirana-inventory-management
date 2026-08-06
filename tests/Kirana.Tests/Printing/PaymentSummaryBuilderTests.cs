using Kirana.Application.Printing;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Printing;

public class PaymentSummaryBuilderTests
{
    [Fact]
    public void CashOnly_ExactAmount_ShowsOnlyCashPaid()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 289m, AmountTendered = 289m, ChangeGiven = 0m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("Cash Paid", row.Label);
        Assert.Equal(289m, row.Amount);
        Assert.False(row.IsDetail);
    }

    [Fact]
    public void CashOnly_WithChange_ShowsCashReceivedAndChangeReturned()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 289m, AmountTendered = 300m, ChangeGiven = 11m },
        ]);

        Assert.Collection(rows,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(289m, r.Amount); Assert.False(r.IsDetail); },
            r => { Assert.Equal("Cash Received", r.Label); Assert.Equal(300m, r.Amount); Assert.True(r.IsDetail); },
            r => { Assert.Equal("Change Returned", r.Label); Assert.Equal(11m, r.Amount); Assert.True(r.IsDetail); });
    }

    [Fact]
    public void UpiOnly_ShowsOnlyTheMethodAndAmount()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 289m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("UPI", row.Label);
        Assert.Equal(289m, row.Amount);
        Assert.DoesNotContain(rows, r => r.Label.Contains("Cash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rows, r => r.Label.Contains("Change", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CardOnly_ShowsOnlyTheMethodAndAmount()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Card, Amount = 500m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("Card", row.Label);
        Assert.Equal(500m, row.Amount);
    }

    [Fact]
    public void FullCustomerCredit_ShowsOnlyCustomerCredit_NoCashRows()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.CustomerCredit, Amount = 289m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("Customer Credit", row.Label);
        Assert.Equal(289m, row.Amount);
    }

    [Fact]
    public void SplitCashAndCustomerCredit_ShowsExactlyCashPaidAndCustomerCredit()
    {
        // The scenario from the bug report: bill ₹289, ₹200 cash + ₹89 Udhaar. Cash was never
        // "tendered" beyond its own ₹200 share, so no Cash Received/Change Returned rows exist —
        // there is nothing in the Payment record to justify them.
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 200m },
            new InvoicePaymentLine { Method = PaymentMethod.CustomerCredit, Amount = 89m },
        ]);

        Assert.Collection(rows,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(200m, r.Amount); },
            r => { Assert.Equal("Customer Credit", r.Label); Assert.Equal(89m, r.Amount); });
    }

    [Fact]
    public void SplitCashAndUpi_TenderedExactly_ShowsOnlyTheTwoMethodRows()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 150m, AmountTendered = 150m },
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 139m },
        ]);

        Assert.Collection(rows,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(150m, r.Amount); },
            r => { Assert.Equal("UPI", r.Label); Assert.Equal(139m, r.Amount); });
    }

    [Fact]
    public void SplitCashAndUpi_CashTenderedMoreThanItsShare_ShowsCashReceivedAndChange()
    {
        // Cash tendered ₹200 against a ₹150 cash share → genuinely ₹50 change was handed back.
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 150m, AmountTendered = 200m, ChangeGiven = 50m },
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 139m },
        ]);

        Assert.Collection(rows,
            r => Assert.Equal("Cash Paid", r.Label),
            r => { Assert.Equal("Cash Received", r.Label); Assert.Equal(200m, r.Amount); },
            r => { Assert.Equal("Change Returned", r.Label); Assert.Equal(50m, r.Amount); },
            r => Assert.Equal("UPI", r.Label));
    }

    [Fact]
    public void SplitCashAndCard_TenderedExactly_ShowsOnlyTheTwoMethodRows()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m },
            new InvoicePaymentLine { Method = PaymentMethod.Card, Amount = 250m },
        ]);

        Assert.Collection(rows,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(100m, r.Amount); },
            r => { Assert.Equal("Card", r.Label); Assert.Equal(250m, r.Amount); });
    }

    [Fact]
    public void MultipleSplitPayments_ThreeWay_ShowsEveryMethodWithNoSpuriousCashRows()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m },
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 50m },
            new InvoicePaymentLine { Method = PaymentMethod.CustomerCredit, Amount = 39m },
        ]);

        Assert.Collection(rows,
            r => { Assert.Equal("Cash Paid", r.Label); Assert.Equal(100m, r.Amount); },
            r => { Assert.Equal("UPI", r.Label); Assert.Equal(50m, r.Amount); },
            r => { Assert.Equal("Customer Credit", r.Label); Assert.Equal(39m, r.Amount); });
    }

    [Fact]
    public void ZeroValuePaymentRows_AreNeverRendered()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 0m },
            new InvoicePaymentLine { Method = PaymentMethod.CustomerCredit, Amount = 289m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("Customer Credit", row.Label);
    }

    [Fact]
    public void CashTenderedEqualToAmount_NeverShowsCashReceivedRow_EvenThoughTenderedIsSet()
    {
        // AmountTendered being present is not itself a reason to show "Cash Received" — only an
        // actual difference from what was paid is.
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 50m, AmountTendered = 50m, ChangeGiven = 0m },
        ]);

        Assert.DoesNotContain(rows, r => r.Label == "Cash Received");
        Assert.DoesNotContain(rows, r => r.Label == "Change Returned");
    }

    [Fact]
    public void ChangeGivenOfZero_IsNeverShown_EvenIfPresentOnTheRecord()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 120m, ChangeGiven = 0m },
        ]);

        Assert.Contains(rows, r => r.Label == "Cash Received");
        Assert.DoesNotContain(rows, r => r.Label == "Change Returned");
    }

    [Fact]
    public void ReferenceNumber_IsAppendedToTheMethodLabel_ForNonCashMethods()
    {
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 200m, ReferenceNumber = "UPI-REF-42" },
        ]);

        Assert.Equal("UPI (Ref: UPI-REF-42)", Assert.Single(rows).Label);
    }

    [Fact]
    public void NonCashMethod_WithSpuriousAmountTendered_StillNeverShowsCashRows()
    {
        // Defensive: SaleService never actually sets AmountTendered on a non-Cash payment, but the
        // builder must not rely on that being true elsewhere — it checks the method explicitly.
        var rows = PaymentSummaryBuilder.Build([
            new InvoicePaymentLine { Method = PaymentMethod.Upi, Amount = 200m, AmountTendered = 250m, ChangeGiven = 50m },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("UPI", row.Label);
        Assert.Equal(200m, row.Amount);
    }
}
