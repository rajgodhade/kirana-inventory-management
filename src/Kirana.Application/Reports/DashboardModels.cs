namespace Kirana.Application.Reports;

/// <summary>
/// The Dashboard's KPI tiles (PRD §51). Split into two groups: figures that move with the selected
/// date range (today's sales, bills, items sold, ...) and figures that are a point-in-time balance
/// regardless of the filter (inventory value, low-stock count, outstanding balances) — a "this
/// week" filter should not make "Customer Outstanding" show last week's balance.
///
/// <see cref="GrossProfit"/> and <see cref="NetProfit"/> are null when the caller lacks
/// <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/> — the rest of the dashboard is
/// still useful to a Manager who is not shown profit figures, so the summary is not refused
/// outright, only the profit-specific fields are withheld (mirrors how purchase price is withheld
/// from Product screens rather than hiding the whole page).
/// </summary>
public sealed class DashboardSummary
{
    public required ReportDateRange Range { get; init; }

    // --- Period figures (respect Range) ---
    public decimal TotalSales { get; init; }
    public decimal TotalPurchases { get; init; }
    public decimal TotalExpenses { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? NetProfit { get; init; }
    public int BillCount { get; init; }
    public decimal ItemsSold { get; init; }

    // --- Point-in-time figures (always "as of now") ---
    public decimal InventoryValue { get; init; }
    public int LowStockCount { get; init; }
    public int OutOfStockCount { get; init; }
    public decimal CustomerOutstanding { get; init; }
    public decimal SupplierOutstanding { get; init; }

    public bool CanViewProfit { get; init; }
}

public sealed class RecentSaleRow
{
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDateUtc { get; init; }
    public string? CustomerName { get; init; }
    public decimal GrandTotal { get; init; }
}

public sealed class RecentPurchaseRow
{
    public int PurchaseId { get; init; }
    public string PurchaseNumber { get; init; } = string.Empty;
    public DateTime PurchaseDateUtc { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
}

public sealed class RecentReturnRow
{
    public string ReturnNumber { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public DateTime ReturnDateUtc { get; init; }
    public decimal Amount { get; init; }
    public bool IsPurchaseReturn { get; init; }
}

public sealed class RecentExpenseRow
{
    public int ExpenseId { get; init; }
    public string ExpenseNumber { get; init; } = string.Empty;
    public DateTime ExpenseDateUtc { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

/// <summary>Every chart the Dashboard shows (PRD §51 "Charts"). <see cref="GrossProfitTrend"/> is
/// empty when the caller lacks <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/>.</summary>
public sealed class DashboardCharts
{
    public required ChartSeries DailySalesTrend { get; init; }
    public required ChartSeries WeeklySales { get; init; }
    public required ChartSeries MonthlySales { get; init; }
    public required ChartSeries SalesVsExpensesSales { get; init; }
    public required ChartSeries SalesVsExpensesExpenses { get; init; }
    public required ChartSeries GrossProfitTrend { get; init; }
    public required ChartSeries ProductCategorySales { get; init; }
    public required ChartSeries PaymentMethodDistribution { get; init; }
    public required ChartSeries TopCustomers { get; init; }
    public required ChartSeries TopSuppliers { get; init; }
}

/// <summary>One row in a "Top N" ranking (customers by revenue, suppliers by purchase value).</summary>
public sealed class RankedPartyRow
{
    public int PartyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int TransactionCount { get; init; }
}
