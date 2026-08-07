using Kirana.App.ViewModels.Reports;
using Kirana.Application.Authentication;
using Kirana.Application.Promotions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Reports;

public sealed partial class PromotionReportView : UserControl
{
    public PromotionReportViewModel ViewModel { get; }
    public PromotionReportView()
    {
        ViewModel = new PromotionReportViewModel(App.Services.GetRequiredService<IPromotionService>(), App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent();
    }
    public Task EnsureLoadedAsync() => ViewModel.LoadAsync();
}
