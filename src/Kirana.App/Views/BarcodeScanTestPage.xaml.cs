using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class BarcodeScanTestPage : Page
{
    public BarcodeScanTestViewModel ViewModel { get; }

    public BarcodeScanTestPage()
    {
        ViewModel = new BarcodeScanTestViewModel(App.Services.GetRequiredService<IBarcodeLookupService>());
        InitializeComponent();
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);
            ScanInputBox.Text = "";
        }
    }
}
