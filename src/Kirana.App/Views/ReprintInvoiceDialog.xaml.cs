using Kirana.App.ViewModels;
using Kirana.Application.Printing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Kirana.App.Views;

/// <summary>Search-by-invoice-number reprint entry point (PRD §23), reachable from Management
/// Home. Finding an invoice is unrestricted. This dialog only searches — it does <em>not</em>
/// nest another <see cref="ContentDialog"/> inside its own button handler (WinUI 3 only supports
/// one open <see cref="ContentDialog"/> per <c>XamlRoot</c> at a time; showing a second one while
/// this one is still open crashes the app). Once <see cref="ShowAsync"/> returns with a result,
/// the caller (<see cref="ManagementPlaceholderPage"/>) drives the manager-authorization step and
/// the invoice preview/print dialog sequentially.</summary>
public sealed partial class ReprintInvoiceDialog : ContentDialog
{
    public ReprintInvoiceViewModel ViewModel { get; }

    public InvoiceDocument? FoundDocument { get; private set; }

    public ReprintInvoiceDialog(IInvoicePrintService invoicePrintService)
    {
        ViewModel = new ReprintInvoiceViewModel(invoicePrintService);
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnInvoiceNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await ViewModel.SearchAsync();
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.HasResult || ViewModel.FoundDocument is not { } document)
        {
            args.Cancel = true;
            await ViewModel.SearchAsync();
            return;
        }

        // Let this dialog close normally (args.Cancel stays false) — the caller opens the
        // authorization + preview dialogs only after this one has fully closed.
        FoundDocument = document;
    }
}
