using Kirana.App.ViewModels;
using Kirana.Application.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class SetupWizardPage : Page
{
    public SetupWizardViewModel ViewModel { get; }

    public SetupWizardPage()
    {
        var setupService = App.Services.GetRequiredService<IFirstTimeSetupService>();
        ViewModel = new SetupWizardViewModel(setupService);
        ViewModel.SetupCompleted += OnSetupCompleted;

        InitializeComponent();
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        Frame.Navigate(typeof(PosShellPage));
    }
}
