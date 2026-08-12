using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.StockCounts;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One row of the stock-count history list.</summary>
public sealed partial class StockCountRowViewModel(StockCountSummary summary) : ObservableObject
{
    public int Id { get; } = summary.Id;
    public string CountNumber { get; } = summary.CountNumber;
    public StockCountStatus Status { get; } = summary.Status;

    public string StartedText { get; } = summary.StartedAtUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");
    public string StatusText { get; } = summary.Status switch
    {
        StockCountStatus.InProgress => "In Progress",
        StockCountStatus.Completed => "Completed",
        _ => "Cancelled",
    };

    public string ItemsText { get; } = summary.ItemCount == summary.CountedItemCount
        ? summary.ItemCount.ToString()
        : $"{summary.CountedItemCount} / {summary.ItemCount}";

    public string VarianceText { get; } = summary.Status == StockCountStatus.InProgress
        ? "Pending"
        : summary.VarianceItemCount == 0 ? "None" : $"{summary.VarianceItemCount} product(s)";

    public string StartedByText { get; } = summary.StartedByUserName ?? "—";

    public bool IsActive { get; } = summary.Status == StockCountStatus.InProgress;
}

/// <summary>
/// One product line on the active-count screen. Holds its own editable text so a half-typed
/// quantity never reaches the service, and exposes the sign/label state the row template needs —
/// text as well as colour, so a shortage is legible without relying on colour alone.
/// </summary>
public sealed partial class StockCountItemRowViewModel : ObservableObject
{
    public int Id { get; }
    public int ProductId { get; }
    public string ProductName { get; }
    public string ProductCode { get; }
    public string UnitText { get; }
    public decimal SystemQuantity { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VarianceText))]
    [NotifyPropertyChangedFor(nameof(VarianceState))]
    [NotifyPropertyChangedFor(nameof(PhysicalText))]
    [NotifyPropertyChangedFor(nameof(IsCounted))]
    private decimal? _countedQuantity;

    /// <summary>What the user is typing. Kept separate from <see cref="CountedQuantity"/> so an
    /// in-progress or invalid entry never becomes a recorded count.</summary>
    [ObservableProperty]
    private string _quantityInput = "";

    public StockCountItemRowViewModel(StockCountItem item)
    {
        Id = item.Id;
        ProductId = item.ProductId;
        ProductName = item.ProductNameSnapshot;
        ProductCode = item.ProductCodeSnapshot;
        UnitText = item.UnitSnapshot.ToDisplayText();
        SystemQuantity = item.SystemQuantity;
        _countedQuantity = item.CountedQuantity;
        _quantityInput = item.CountedQuantity?.ToString("0.###") ?? "";
    }

    public string SystemText => SystemQuantity.ToString("0.###");
    public string PhysicalText => CountedQuantity?.ToString("0.###") ?? "—";
    public bool IsCounted => CountedQuantity is not null;

    public decimal? Variance => CountedQuantity is null ? null : CountedQuantity - SystemQuantity;

    /// <summary>Always carries an explicit sign, so surplus and shortage are distinguishable
    /// without colour (§29).</summary>
    public string VarianceText => Variance switch
    {
        null => "—",
        0m => "0",
        > 0m => $"+{Variance.Value:0.###}",
        _ => Variance.Value.ToString("0.###"),
    };

    /// <summary>Drives the semantic brush: neutral / positive / negative.</summary>
    public string VarianceState => Variance switch
    {
        null => "Pending",
        0m => "Match",
        > 0m => "Surplus",
        _ => "Shortage",
    };
}

/// <summary>A product-search hit while counting: just enough to recognise the product and add it.</summary>
public sealed class StockCountSearchRowViewModel(Product product)
{
    public int Id { get; } = product.Id;
    public string Name { get; } = product.Name;

    public string DetailText { get; } = string.IsNullOrWhiteSpace(product.Sku)
        ? product.ProductCode
        : $"{product.ProductCode} · {product.Sku}";

    public string StockText { get; } =
        $"{product.Inventory?.QuantityOnHand ?? 0m:0.###} {product.Unit.ToDisplayText()}";
}

/// <summary>One line of the variance-review screen (§19).</summary>
public sealed class StockCountVarianceRowViewModel(StockCountVarianceLine line)
{
    public string ProductName { get; } = line.ProductName;
    public string ProductCode { get; } = line.ProductCode;
    public string UnitText { get; } = line.Unit.ToDisplayText();

    public string TransitionText { get; } =
        $"{line.SystemQuantity:0.###} → {line.CountedQuantity ?? line.SystemQuantity:0.###}";

    public string VarianceText { get; } = line.ObservedVariance switch
    {
        null => "—",
        0m => "0",
        > 0m => $"+{line.ObservedVariance.Value:0.###}",
        _ => line.ObservedVariance.Value.ToString("0.###"),
    };

    public string VarianceState { get; } = line.ObservedVariance switch
    {
        null => "Pending",
        0m => "Match",
        > 0m => "Surplus",
        _ => "Shortage",
    };

    public bool WillRebase { get; } = line.WillRebase;

    /// <summary>Shown only on rebased lines, so the operator understands why the applied figure
    /// differs from the variance they wrote down.</summary>
    public string RebaseNote { get; } = line.WillRebase
        ? $"Stock changed to {line.CurrentSystemQuantity:0.###} during the count — will adjust by " +
          $"{(line.AppliedAdjustment > 0 ? "+" : "")}{line.AppliedAdjustment:0.###} to reach {line.ResultingQuantity:0.###}."
        : "";
}
