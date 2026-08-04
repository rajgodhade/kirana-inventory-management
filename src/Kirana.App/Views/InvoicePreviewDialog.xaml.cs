using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>Shows a rendered invoice and lets the user print it (PRD §23). "Print" never closes
/// this dialog — a failed print, offline printer, or cancelled system dialog should let the user
/// change format/printer and try again without re-completing the sale or reopening this screen.
/// The user closes explicitly via "Close" once done (or without ever printing at all).</summary>
public sealed partial class InvoicePreviewDialog : ContentDialog
{
    public InvoicePreviewViewModel ViewModel { get; }

    public InvoicePreviewDialog(InvoicePreviewViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
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
}
