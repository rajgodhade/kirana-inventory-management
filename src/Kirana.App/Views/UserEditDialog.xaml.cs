using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class UserEditDialog : ContentDialog
{
    public UserEditViewModel ViewModel { get; }

    public UserEditDialog(UserEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = viewModel.DialogTitle;
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveCommand.ExecuteAsync(null);
            if (ViewModel.ErrorMessage is not null)
            {
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
