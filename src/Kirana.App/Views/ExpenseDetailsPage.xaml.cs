using Kirana.App.Printing;
using Kirana.Application.Authentication;
using Kirana.Application.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

/// <summary>
/// One expense, shown from the same <see cref="ExpenseReceiptDocument"/> the printed voucher uses,
/// so the screen and the slip can never disagree.
/// </summary>
public sealed partial class ExpenseDetailsPage : Page
{
    private int _expenseId;

    public ExpenseDetailsPage() => InitializeComponent();

    private int? CurrentUserId => App.Services.GetRequiredService<ManagementSession>().CurrentUser?.Id;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _expenseId = (int)e.Parameter;

        try
        {
            var document = await App.Services.GetRequiredService<IReturnReceiptService>()
                .GetExpenseReceiptAsync(_expenseId, CurrentUserId);

            TitleText.Text = document.ExpenseNumber;
            SubtitleText.Text = document.CategoryName;
            AmountText.Text = "₹" + document.Amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
            CategoryText.Text = document.CategoryName;
            DateText.Text = document.ExpenseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy");
            MethodText.Text = document.PaymentMethod;
            ReferenceText.Text = string.IsNullOrWhiteSpace(document.ReferenceNumber) ? "—" : document.ReferenceNumber;
            RecordedByText.Text = string.IsNullOrWhiteSpace(document.RecordedByName) ? "—" : document.RecordedByName;
            DescriptionText.Text = document.Description ?? string.Empty;
            NotesText.Text = document.Notes ?? string.Empty;
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var receiptService = App.Services.GetRequiredService<IReturnReceiptService>();
        try
        {
            var document = await receiptService.GetExpenseReceiptAsync(_expenseId, CurrentUserId);

            using var helper = new ExpenseReceiptPrintHelper(App.MainWindow, document, InvoiceFormat.Thermal80mm);
            await helper.ShowPrintUIAsync();

            await receiptService.LogExpensePrintAsync(_expenseId, CurrentUserId);
        }
        catch (Exception ex)
        {
            ErrorBar.Message = $"Could not print the voucher: {ex.Message}. The expense is unaffected.";
            ErrorBar.IsOpen = true;
        }
    }
}
