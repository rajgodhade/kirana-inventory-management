using Kirana.Application.Authentication;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>
/// The "Management Access" unlock dialog (PRD §7): username+password, or PIN-only if
/// username is left blank.
/// </summary>
public sealed partial class ManagementLoginDialog : ContentDialog
{
    private readonly IAuthenticationService _authService;

    public bool Unlocked { get; private set; }

    public ManagementLoginDialog(IAuthenticationService authService)
    {
        _authService = authService;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var username = UsernameBox.Text;
            var secret = PasswordBox.Password;

            // A numeric secret is a PIN even when a username is supplied. This keeps the
            // username field useful for selecting the account while allowing the normal
            // four-to-six digit management PIN entered in this dialog.
            var isPin = secret.Length is >= 4 and <= 6 && secret.All(char.IsDigit);
            var result = string.IsNullOrWhiteSpace(username)
                ? await _authService.LoginWithPinAsync(secret)
                : isPin
                    ? await _authService.LoginWithUsernameAndPinAsync(username, secret)
                    : await _authService.LoginWithPasswordAsync(username, secret);

            if (!result.Success)
            {
                ErrorBar.Message = result.ErrorMessage;
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            Unlocked = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
