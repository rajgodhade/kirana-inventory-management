using Kirana.Application.Authentication;
using Kirana.Application.Returns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

/// <summary>Read-only view of one purchase return.</summary>
public sealed partial class PurchaseReturnDetailsPage : Page
{
    public PurchaseReturnDetailsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var userId = App.Services.GetRequiredService<ManagementSession>().CurrentUser?.Id;

        try
        {
            var purchaseReturn = await App.Services.GetRequiredService<IPurchaseReturnService>()
                .GetByIdAsync((int)e.Parameter, userId);

            if (purchaseReturn is null)
            {
                ErrorBar.Message = "That purchase return could not be found.";
                ErrorBar.IsOpen = true;
                return;
            }

            TitleText.Text = purchaseReturn.ReturnNumber;
            SubtitleText.Text = $"Against {purchaseReturn.PurchaseNumberSnapshot} · {purchaseReturn.ReturnDateUtc.ToLocalTime():dd-MMM-yyyy hh:mm tt}";
            TotalText.Text = "₹" + purchaseReturn.TotalReturnAmount.ToString(
                "N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
            SupplierText.Text = $"{purchaseReturn.Supplier.Name} · {purchaseReturn.Supplier.SupplierCode}";
            ReasonText.Text = string.IsNullOrWhiteSpace(purchaseReturn.Reason) ? string.Empty : $"Reason: {purchaseReturn.Reason}";
            LinesList.ItemsSource = purchaseReturn.Items.ToList();
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }
}
