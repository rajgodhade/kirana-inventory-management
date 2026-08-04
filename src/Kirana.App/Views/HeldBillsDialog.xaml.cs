using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class HeldBillsDialog : ContentDialog
{
    public HeldBillsViewModel ViewModel { get; }

    public int? ResumedHeldBillId => ViewModel.ResumedHeldBillId;

    public HeldBillsDialog(PosShellViewModel owner)
    {
        ViewModel = new HeldBillsViewModel(owner);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void OnResumeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is HeldBill bill)
        {
            ViewModel.SelectForResume(bill);
            Hide();
        }
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
