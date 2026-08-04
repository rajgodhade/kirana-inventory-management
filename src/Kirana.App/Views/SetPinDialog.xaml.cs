using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class SetPinDialog : ContentDialog
{
    private readonly UserManagementViewModel _viewModel;
    private readonly int _userId;

    public SetPinDialog(UserManagementViewModel viewModel, int userId)
    {
        _viewModel = viewModel;
        _userId = userId;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var succeeded = await _viewModel.SetPinAsync(_userId, PinBox.Password);
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

    private async void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var succeeded = await _viewModel.SetPinAsync(_userId, null);
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
