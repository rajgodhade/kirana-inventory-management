using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>Shows a rendered invoice and lets the user print it (PRD §23). "Print" never closes
/// this dialog — a failed print, offline printer, or cancelled system dialog should let the user
/// change format/printer and try again without re-completing the sale or reopening this screen.
/// The user closes explicitly via "Close" once done (or without ever printing at all).</summary>
public sealed partial class InvoicePreviewDialog : ContentDialog
{
    private readonly int _automaticPrintCopies;
    public InvoicePreviewViewModel ViewModel { get; }

    public InvoicePreviewDialog(InvoicePreviewViewModel viewModel, int automaticPrintCopies = 0)
    {
        ViewModel = viewModel;
        _automaticPrintCopies = Math.Clamp(automaticPrintCopies, 0, 2);
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        for (var copy = 0; copy < _automaticPrintCopies; copy++)
        {
            await ViewModel.PrintCommand.ExecuteAsync(null);
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.PrintCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
