using Kirana.App.ViewModels;
using Kirana.Application.Expenses;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ExpenseEditDialog : ContentDialog
{
    private readonly ExpensesViewModel _owner;
    private readonly Expense? _existing;

    public ExpenseEditDialog(ExpensesViewModel owner, Expense? existing)
    {
        _owner = owner;
        _existing = existing;

        InitializeComponent();
        DialogTitleText.Text = existing is null ? "Add Expense" : $"Edit {existing.ExpenseNumber}";

        CategoryBox.ItemsSource = owner.Categories;

        if (existing is null)
        {
            DateBox.Date = DateTimeOffset.Now;
            CategoryBox.SelectedItem = owner.Categories.FirstOrDefault();
        }
        else
        {
            CategoryBox.SelectedItem = owner.Categories.FirstOrDefault(c => c.Id == existing.ExpenseCategoryId);
            AmountBox.Text = existing.Amount.ToString("0.00");
            DateBox.Date = new DateTimeOffset(existing.ExpenseDateUtc.ToLocalTime());
            MethodBox.SelectedIndex = existing.PaymentMethod switch
            {
                PaymentMethod.Upi => 1,
                PaymentMethod.Card => 2,
                _ => 0,
            };
            DescriptionBox.Text = existing.Description ?? string.Empty;
            ReferenceBox.Text = existing.ReferenceNumber ?? string.Empty;
            NotesBox.Text = existing.Notes ?? string.Empty;
        }

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (CategoryBox.SelectedItem is not ExpenseCategory category)
            {
                Fail("Choose a category.");
                args.Cancel = true;
                return;
            }

            if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            {
                Fail("Enter an amount greater than zero.");
                args.Cancel = true;
                return;
            }

            var date = (DateBox.Date ?? DateTimeOffset.Now).UtcDateTime;
            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "Upi" => PaymentMethod.Upi,
                "Card" => PaymentMethod.Card,
                _ => PaymentMethod.Cash,
            };

            try
            {
                if (_existing is null)
                {
                    await _owner.CreateAsync(new CreateExpenseRequest
                    {
                        ExpenseCategoryId = category.Id,
                        Amount = amount,
                        ExpenseDateUtc = date,
                        PaymentMethod = method,
                        Description = DescriptionBox.Text,
                        ReferenceNumber = ReferenceBox.Text,
                        Notes = NotesBox.Text,
                        PerformedByUserId = _owner.CurrentUserId,
                    });
                }
                else
                {
                    await _owner.UpdateAsync(_existing.Id, new UpdateExpenseRequest
                    {
                        ExpenseCategoryId = category.Id,
                        Amount = amount,
                        ExpenseDateUtc = date,
                        PaymentMethod = method,
                        Description = DescriptionBox.Text,
                        ReferenceNumber = ReferenceBox.Text,
                        Notes = NotesBox.Text,
                        PerformedByUserId = _owner.CurrentUserId,
                    });
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Fail(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
