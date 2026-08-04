using Kirana.App.ViewModels.Reports;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Reports;

public sealed partial class DashboardView : UserControl
{
    public DashboardTabViewModel ViewModel { get; }

    public DashboardView()
    {
        var services = App.Services;
        ViewModel = new DashboardTabViewModel(
            services.GetRequiredService<IDashboardService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
    }

    /// <summary>Called once by <see cref="ReportsHubPage"/> the first time this tab is shown.</summary>
    public Task EnsureLoadedAsync() => ViewModel.LoadAsync();

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnDateFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnDateFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => await ViewModel.LoadAsync();
}
