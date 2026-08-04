using Kirana.App.ViewModels;
using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class AuditLogPage : Page
{
    public AuditLogViewModel ViewModel { get; }

    public AuditLogPage()
    {
        var services = App.Services;
        ViewModel = new AuditLogViewModel(
            services.GetRequiredService<IAuditLogQueryService>(),
            services.GetRequiredService<IUserManagementService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFilters();
        await ViewModel.SearchAsync();
    }
}
