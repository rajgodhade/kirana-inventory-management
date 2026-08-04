using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>Bill-level discount percent entry (PRD §19). Authorization for large discounts is
/// enforced by the caller (<see cref="PosShellPage"/>) via <see cref="ManagerAuthorizationDialog"/>
/// before this value is committed — this dialog only collects the requested percent.</summary>
public sealed partial class BillDiscountDialog : ContentDialog
{
    public decimal Percent { get; private set; }

    public BillDiscountDialog(decimal currentPercent)
    {
        InitializeComponent();
        PercentBox.Text = currentPercent.ToString("0.##");
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!decimal.TryParse(PercentBox.Text, out var value) || value < 0 || value > 100)
        {
            ErrorBar.Message = "Enter a discount percent between 0 and 100.";
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        Percent = value;
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
