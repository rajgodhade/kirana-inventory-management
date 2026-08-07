using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Promotions;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class PromotionEditDialog : ContentDialog
{
    public PromotionEditViewModel ViewModel { get; }
    public PromotionEditDialog(int? promotionId)
    {
        var services = App.Services;
        ViewModel = new PromotionEditViewModel(services.GetRequiredService<IPromotionService>(), services.GetRequiredService<ICategoryService>(),
            services.GetRequiredService<IBrandService>(), services.GetRequiredService<IProductService>(), services.GetRequiredService<ManagementSession>(), promotionId);
        InitializeComponent();
        DialogTitleText.Text = promotionId is null ? "Create Promotion" : "Edit Promotion";
        PrimaryButtonClick += OnPrimaryButtonClick;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.LoadAsync();
            foreach (var id in ViewModel.ExistingTargetIds)
            {
                object? item = ViewModel.SelectedScopeType switch
                {
                    PromotionScopeType.Category => ViewModel.Categories.FirstOrDefault(x => x.Id == id),
                    PromotionScopeType.Brand => ViewModel.Brands.FirstOrDefault(x => x.Id == id),
                    PromotionScopeType.Product => ViewModel.Products.FirstOrDefault(x => x.Id == id),
                    _ => null,
                };
                if (item is null) continue;
                if (ViewModel.IsCategoryScope) CategoryTargets.SelectedItems.Add(item);
                else if (ViewModel.IsBrandScope) BrandTargets.SelectedItems.Add(item);
                else if (ViewModel.IsProductScope) ProductTargets.SelectedItems.Add(item);
            }
        }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            IReadOnlyList<int> ids = ViewModel.SelectedScopeType switch
            {
                PromotionScopeType.Category => CategoryTargets.SelectedItems.OfType<Category>().Select(x => x.Id).ToList(),
                PromotionScopeType.Brand => BrandTargets.SelectedItems.OfType<Brand>().Select(x => x.Id).ToList(),
                PromotionScopeType.Product => ProductTargets.SelectedItems.OfType<Product>().Select(x => x.Id).ToList(),
                _ => [],
            };
            await ViewModel.SaveAsync(ids);
            args.Cancel = ViewModel.ErrorMessage is not null;
        }
        finally { deferral.Complete(); }
    }

    private void OnPreviewInputChanged(object sender, TextChangedEventArgs e) => ViewModel.UpdatePreview();
    private void OnProductSearchChanged(object sender, TextChangedEventArgs e) => ViewModel.FilterProducts((sender as TextBox)?.Text);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
