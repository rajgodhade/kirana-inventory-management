using Kirana.Application.Authentication;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>PIN-only step-up authorization for a single sensitive POS action (e.g. a large
/// discount) — verifies PIN + permission via <see cref="IAuthenticationService.AuthorizeAsync"/>
/// without unlocking the full Management <see cref="ManagementSession"/>.</summary>
public sealed partial class ManagerAuthorizationDialog : ContentDialog
{
    private readonly IAuthenticationService _authService;
    private readonly string _requiredPermission;

    public int? AuthorizedUserId { get; private set; }

    public ManagerAuthorizationDialog(IAuthenticationService authService, string requiredPermission)
    {
        _authService = authService;
        _requiredPermission = requiredPermission;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var result = await _authService.AuthorizeAsync(PinBox.Password, _requiredPermission);
            if (!result.Success)
            {
                ErrorBar.Message = result.ErrorMessage;
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            AuthorizedUserId = result.User!.Id;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
