using System.Globalization;
using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Views;

public sealed partial class CashRegisterPage : Page
{
    public CashRegisterViewModel ViewModel { get; }

    public CashRegisterPage()
    {
        var services = App.Services;
        ViewModel = new CashRegisterViewModel(
            services.GetRequiredService<ICashRegisterService>(),
            services.GetRequiredService<ManagementSession>());
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnOpenRegisterClick(object sender, RoutedEventArgs e)
    {
        if (!TryAmount(OpeningCashBox.Text, out var amount))
        {
            ViewModel.ErrorMessage = "Enter a valid, non-negative opening cash amount.";
            return;
        }

        if (await ViewModel.OpenAsync(amount)) OpeningCashBox.Text = string.Empty;
    }

    private async void OnCashInClick(object sender, RoutedEventArgs e) =>
        await ShowMovementDialogAsync(CashMovementType.CashIn);

    private async void OnCashOutClick(object sender, RoutedEventArgs e) =>
        await ShowMovementDialogAsync(CashMovementType.CashOut);

    private static readonly string[] CashInReasons =
        ["Change / float top-up", "Owner cash deposit", "Petty cash returned", "Advance recovered"];

    private static readonly string[] CashOutReasons =
        ["Bank deposit", "Owner withdrawal", "Supplier payment", "Staff advance", "Petty expense"];

    private async Task ShowMovementDialogAsync(CashMovementType type)
    {
        var amountBox = new TextBox { Header = "Amount", PlaceholderText = "0.00" };
        var reasonBox = new TextBox { Header = "Reason", PlaceholderText = "Pick a common reason below, or type your own", MaxLength = 500, TextWrapping = TextWrapping.Wrap };

        var chipsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var preset in type == CashMovementType.CashIn ? CashInReasons : CashOutReasons)
        {
            var chip = new Button
            {
                Content = preset,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(999),
                FontSize = 12,
                Background = Brush("SurfaceAltBrush"),
                BorderBrush = Brush("SurfaceBorderBrush"),
                BorderThickness = new Thickness(1),
            };
            // Fills the box rather than appending, but it stays a plain editable TextBox — the
            // cashier can still tweak or replace it, this just saves typing the common case.
            chip.Click += (_, _) => reasonBox.Text = preset;
            chipsRow.Children.Add(chip);
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = type == CashMovementType.CashIn
                ? "Record physical cash added to the open drawer."
                : "Confirm physical cash removed from the open drawer.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(amountBox);
        content.Children.Add(new ScrollViewer
        {
            Content = chipsRow,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        content.Children.Add(reasonBox);

        var dialog = new ContentDialog
        {
            Title = type == CashMovementType.CashIn ? "Cash In" : "Cash Out",
            Content = content,
            PrimaryButtonText = type == CashMovementType.CashIn ? "Record Cash In" : "Confirm Cash Out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        }.Themed(XamlRoot).Large(600, 460);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!TryPositiveAmount(amountBox.Text, out var amount) || string.IsNullOrWhiteSpace(reasonBox.Text))
        {
            ViewModel.ErrorMessage = "Enter an amount greater than zero and a reason.";
            return;
        }

        await ViewModel.RecordMovementAsync(type, amount, reasonBox.Text, Guid.NewGuid());
    }

    private async void OnXReportClick(object sender, RoutedEventArgs e)
    {
        try { await ShowReportAsync(await ViewModel.GetXReportAsync(), "X Report (register remains open)"); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async void OnCloseRegisterClick(object sender, RoutedEventArgs e)
    {
        CashRegisterReport current;
        try { current = await ViewModel.GetXReportAsync(); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; return; }

        var actualBox = new TextBox { Header = "Counted cash", PlaceholderText = "0.00", Text = current.ExpectedCash.ToString("0.00", CultureInfo.InvariantCulture) };
        var varianceText = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        void UpdateVariance()
        {
            if (!TryAmount(actualBox.Text, out var actual))
            {
                varianceText.Text = "Cash difference: enter a valid amount";
                return;
            }

            var difference = actual - current.ExpectedCash;
            varianceText.Text = difference switch
            {
                0m => "Cash matches",
                < 0m => $"Short by {CashRegisterViewModel.Currency(Math.Abs(difference))}",
                _ => $"Over by {CashRegisterViewModel.Currency(difference)}",
            };
        }
        actualBox.TextChanged += (_, _) => UpdateVariance();
        UpdateVariance();

        var content = BuildPolishedReportSummary(current, includeMovementDetails: false);
        content.Children.Add(new Border { Height = 1, Opacity = 0.35, Margin = new Thickness(0, 6, 0, 6), Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceBorderBrush"] });
        content.Children.Add(actualBox);
        content.Children.Add(varianceText);

        var countDialog = new ContentDialog
        {
            Title = "Count & Close Register",
            Content = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        }.Themed(XamlRoot).Large(940, 780);
        if (await countDialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!TryAmount(actualBox.Text, out var counted))
        {
            ViewModel.ErrorMessage = "Enter a valid, non-negative counted cash amount.";
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Close this register?",
            Content = $"Expected {CashRegisterViewModel.Currency(current.ExpectedCash)}\nCounted {CashRegisterViewModel.Currency(counted)}\nDifference {CashRegisterViewModel.Currency(counted - current.ExpectedCash)}\n\nClosing creates an immutable Z Report.",
            PrimaryButtonText = "Close Register",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var report = await ViewModel.CloseAsync(counted);
        if (report is not null) await ShowReportAsync(report, "Z Report");
    }

    private async void OnViewZReportClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int id) return;
        try { await ShowReportAsync(await ViewModel.GetZReportAsync(id), "Z Report"); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async Task ShowReportAsync(CashRegisterReport report, string title)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = BuildPolishedReportSummary(report, includeMovementDetails: true),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Print",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot).Large(940, 780);
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var export = BuildPrintData(report, title);
        using var helper = new ReportPrintHelper(App.MainWindow, export);
        try { await helper.ShowPrintUIAsync(); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not open printing: {ex.Message}"; }
    }

    private static StackPanel BuildPolishedReportSummary(CashRegisterReport x, bool includeMovementDetails)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 640 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{x.RegisterName} · {x.BusinessDate:dd MMM yyyy}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextSecondaryBrush"),
        });
        AddReportLine(panel, "Opened", $"{x.OpenedBy} · {x.OpenedAtUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}");
        if (x.ClosedAtUtc is not null)
        {
            AddReportLine(panel, "Closed", $"{x.ClosedBy} · {x.ClosedAtUtc.Value.ToLocalTime():dd MMM yyyy, hh:mm tt}");
        }

        var sales = new StackPanel { Spacing = 6 };
        AddSectionTitle(sales, "Sales Summary");
        AddReportLine(sales, "Total Sales", Money(x.TotalSales), strong: true);
        AddReportLine(sales, "Bills", x.BillCount.ToString(CultureInfo.InvariantCulture));
        AddReportLine(sales, "Cash Sales", Money(x.CashSales), valueBrush: Brush("SuccessBrush"));
        AddReportLine(sales, "UPI", Money(x.UpiSales));
        AddReportLine(sales, "Card", Money(x.CardSales));
        AddReportLine(sales, "Udhaar", Money(x.CustomerCreditSales));

        var drawer = new StackPanel { Spacing = 6 };
        AddSectionTitle(drawer, "Cash Drawer");
        AddReportLine(drawer, "Opening Cash", Money(x.OpeningCash));
        AddReportLine(drawer, "Cash Sales", Money(x.CashSales), valueBrush: Brush("SuccessBrush"));
        AddReportLine(drawer, "Udhaar Repayments", Money(x.CashCreditRepayments), valueBrush: x.CashCreditRepayments == 0m ? null : Brush("SuccessBrush"));
        AddReportLine(drawer, "Cash In", Money(x.CashIn), valueBrush: x.CashIn == 0m ? null : Brush("SuccessBrush"));
        AddReportLine(drawer, "Cash Refunds", SignedOutflow(x.CashRefunds), valueBrush: x.CashRefunds == 0m ? null : Brush("DangerBrush"));
        AddReportLine(drawer, "Supplier Payments", SignedOutflow(x.SupplierCashPayments), valueBrush: x.SupplierCashPayments == 0m ? null : Brush("WarningBrush"));
        AddReportLine(drawer, "Cash Out", SignedOutflow(x.CashOut), valueBrush: x.CashOut == 0m ? null : Brush("WarningBrush"));
        drawer.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 2), Background = Brush("SurfaceBorderBrush"), Opacity = 0.55 });
        AddReportLine(drawer, "EXPECTED CASH", Money(x.ExpectedCash), strong: true, valueBrush: Brush("AccentFillColorDefaultBrush"), large: true);
        if (x.ActualCash is not null) AddReportLine(drawer, "Counted Cash", Money(x.ActualCash.Value));
        if (x.Variance is not null) AddReportLine(drawer, "Difference", Money(x.Variance.Value), strong: true);

        // Side by side rather than stacked: the two cards are roughly equal height, so this
        // keeps the whole report on one screen instead of forcing the dialog to scroll.
        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var salesCard = WrapSection(sales);
        var drawerCard = WrapSection(drawer);
        Grid.SetColumn(drawerCard, 1);
        columns.Children.Add(salesCard);
        columns.Children.Add(drawerCard);
        panel.Children.Add(columns);

        if (includeMovementDetails && x.Movements.Count > 0)
        {
            var movements = new StackPanel { Spacing = 8 };
            // Not strictly "manual" any more: supplier cash payments land here too, derived from
            // the payments themselves rather than typed in by the cashier.
            AddSectionTitle(movements, "Cash Movements");
            foreach (var m in x.Movements)
            {
                var grid = new Grid { ColumnSpacing = 16 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                grid.Children.Add(new TextBlock
                {
                    Text = m.OccurredAtUtc.ToLocalTime().ToString("hh:mm tt", CultureInfo.InvariantCulture),
                    Foreground = Brush("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Top,
                    MinWidth = 70,
                });

                var detail = new StackPanel { Spacing = 2 };
                detail.Children.Add(new TextBlock { Text = MovementLabel(m.Type), FontWeight = FontWeights.SemiBold });
                detail.Children.Add(new TextBlock
                {
                    Text = $"{m.Reason} · {m.PerformedBy}",
                    Foreground = Brush("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
                Grid.SetColumn(detail, 1);
                grid.Children.Add(detail);

                var amount = new TextBlock
                {
                    // Inflow reads green, every outflow reads amber — supplier payments are told
                    // apart by their label and signed amount, not by a third colour.
                    Text = m.Type == CashRegisterMovementKind.CashIn ? Money(m.Amount) : SignedOutflow(m.Amount),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = m.Type == CashRegisterMovementKind.CashIn ? Brush("SuccessBrush") : Brush("WarningBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                Grid.SetColumn(amount, 2);
                grid.Children.Add(amount);
                movements.Children.Add(grid);
            }
            panel.Children.Add(WrapSection(movements));
        }

        return panel;
    }

    private static Border WrapSection(UIElement child) => new()
    {
        Padding = new Thickness(12),
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = Brush("SurfaceBorderBrush"),
        Background = Brush("SurfaceBrush"),
        Child = child,
    };

    private static void AddSectionTitle(StackPanel panel, string title) =>
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 18 });

    private static void AddReportLine(StackPanel panel, string label, string value, bool strong = false, Brush? valueBrush = null, bool large = false)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            // A brush explicitly set to null paints nothing in WinUI — it is not the same as
            // leaving Foreground unset, which falls back to the platform default. Every row
            // with no special colour needs a real brush or its text renders invisible.
            Foreground = strong ? Brush("TextPrimaryBrush") : Brush("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var valueText = new TextBlock
        {
            Text = value,
            FontWeight = strong ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = large ? 22 : 16,
            Foreground = valueBrush ?? Brush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        panel.Children.Add(grid);
    }

    private static string MovementLabel(CashRegisterMovementKind type) => type switch
    {
        CashRegisterMovementKind.CashIn => "Cash In",
        CashRegisterMovementKind.SupplierPayment => "Supplier Payment",
        _ => "Cash Out",
    };

    private static string Money(decimal value)
    {
        var rounded = NormalizeMoney(value);
        var prefix = rounded < 0m ? "-\u20B9" : "\u20B9";
        return prefix + Math.Abs(rounded).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
    }

    private static string SignedOutflow(decimal amount) =>
        NormalizeMoney(amount) == 0m ? Money(0m) : Money(-amount);

    private static decimal NormalizeMoney(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0m ? 0m : rounded;
    }

    private static Brush? Brush(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Brush
            : null;

    private static StackPanel BuildReportSummary(CashRegisterReport x, bool includeMovementDetails)
    {
        var panel = new StackPanel { Spacing = 7, MinWidth = 480 };
        panel.Children.Add(new TextBlock { Text = $"{x.RegisterName} · {x.BusinessDate:dd MMM yyyy}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddLine(panel, "Opened", $"{x.OpenedBy} · {x.OpenedAtUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}");
        if (x.ClosedAtUtc is not null) AddLine(panel, "Closed", $"{x.ClosedBy} · {x.ClosedAtUtc.Value.ToLocalTime():dd MMM yyyy, hh:mm tt}");
        AddLine(panel, "Opening cash", CashRegisterViewModel.Currency(x.OpeningCash));
        AddLine(panel, "Total sales", $"{CashRegisterViewModel.Currency(x.TotalSales)} · {x.BillCount} bill(s)");
        AddLine(panel, "Cash sales", CashRegisterViewModel.Currency(x.CashSales));
        AddLine(panel, "Total returns", CashRegisterViewModel.Currency(x.TotalReturns));
        AddLine(panel, "Udhaar repayments", CashRegisterViewModel.Currency(x.CashCreditRepayments));
        AddLine(panel, "Cash in", CashRegisterViewModel.Currency(x.CashIn));
        AddLine(panel, "Cash refunds", "−" + CashRegisterViewModel.Currency(x.CashRefunds));
        AddLine(panel, "Cash out", "−" + CashRegisterViewModel.Currency(x.CashOut));
        AddLine(panel, "Expected cash", CashRegisterViewModel.Currency(x.ExpectedCash), true);
        AddLine(panel, "Non-cash sales", $"UPI {CashRegisterViewModel.Currency(x.UpiSales)} · Card {CashRegisterViewModel.Currency(x.CardSales)} · Credit {CashRegisterViewModel.Currency(x.CustomerCreditSales)}");
        if (x.ActualCash is not null) AddLine(panel, "Counted cash", CashRegisterViewModel.Currency(x.ActualCash.Value));
        if (x.Variance is not null) AddLine(panel, "Difference", CashRegisterViewModel.Currency(x.Variance.Value), true);
        if (includeMovementDetails && x.Movements.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Manual movements", Margin = new Thickness(0, 8, 0, 0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var m in x.Movements)
                panel.Children.Add(new TextBlock { Text = $"{m.OccurredAtUtc.ToLocalTime():hh:mm tt} · {m.Type} · {CashRegisterViewModel.Currency(m.Amount)} · {m.Reason} · {m.PerformedBy}", TextWrapping = TextWrapping.Wrap });
        }
        return panel;
    }

    private static void AddLine(StackPanel panel, string label, string value, bool strong = false)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal });
        var valueText = new TextBlock { Text = value, FontWeight = strong ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        panel.Children.Add(grid);
    }

    private static ReportExportData BuildPrintData(CashRegisterReport x, string title)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Opening cash", Money(x.OpeningCash) },
            new[] { "Total sales", Money(x.TotalSales) },
            new[] { "Bills", x.BillCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Cash sales", Money(x.CashSales) },
            new[] { "UPI", Money(x.UpiSales) },
            new[] { "Card", Money(x.CardSales) },
            new[] { "Udhaar", Money(x.CustomerCreditSales) },
            new[] { "Udhaar repayments", Money(x.CashCreditRepayments) },
            new[] { "Cash in", Money(x.CashIn) },
            new[] { "Cash refunds", SignedOutflow(x.CashRefunds) },
            new[] { "Supplier payments", SignedOutflow(x.SupplierCashPayments) },
            new[] { "Cash out", SignedOutflow(x.CashOut) },
            new[] { "Expected cash", Money(x.ExpectedCash) },
            new[] { "Counted cash", x.ActualCash is null ? "—" : CashRegisterViewModel.Currency(x.ActualCash.Value) },
            new[] { "Difference", x.Variance is null ? "—" : CashRegisterViewModel.Currency(x.Variance.Value) },
        };
        return new ReportExportData
        {
            Title = $"{title} · {x.RegisterName}",
            Subtitle = $"{x.BusinessDate:dd MMM yyyy} · Opened by {x.OpenedBy}",
            Columns = new[] { "Measure", "Amount" },
            Rows = rows,
        };
    }

    private static bool TryPositiveAmount(string? text, out decimal amount) => TryAmount(text, out amount) && amount > 0m;

    private static bool TryAmount(string? text, out decimal amount)
    {
        var styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
        return (decimal.TryParse(text, styles, CultureInfo.CurrentCulture, out amount)
                || decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out amount))
            && amount >= 0m;
    }
}
