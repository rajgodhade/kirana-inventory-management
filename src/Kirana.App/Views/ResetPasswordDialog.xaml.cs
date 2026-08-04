using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ResetPasswordDialog : ContentDialog
{
    private readonly UserManagementViewModel _viewModel;
    private readonly int _userId;

    public ResetPasswordDialog(UserManagementViewModel viewModel, int userId)
    {
        _viewModel = viewModel;
        _userId = userId;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                ErrorBar.Message = "Passwords do not match.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            var succeeded = await _viewModel.ResetPasswordAsync(_userId, NewPasswordBox.Password);
            if (!succeeded)
            {
                ErrorBar.Message = _viewModel.ErrorMessage;
                ErrorBar.IsOpen = true;
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
