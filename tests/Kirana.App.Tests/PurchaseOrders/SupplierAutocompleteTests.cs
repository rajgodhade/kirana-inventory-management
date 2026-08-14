using Kirana.App.Tests.TestSupport;
using Kirana.App.ViewModels;

namespace Kirana.App.Tests.PurchaseOrders;

/// <summary>
/// The supplier suggestion popup used to stay open on top of the committed supplier. These pin the
/// open/closed lifecycle (§1–§4) and its independence from the product picker (§19, §21).
/// </summary>
public sealed class SupplierAutocompleteTests
{
    private readonly PurchaseOrderEntryFixture _fixture = new();

    private async Task<PurchaseOrderEntryViewModel> CreateAsync()
    {
        _fixture.AddSupplier("Kumar Supplier", "SUP-000001");
        _fixture.AddSupplier("Sharma Distributors", "SUP-000002");
        var viewModel = _fixture.CreateViewModel();
        await viewModel.InitializeAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task SelectingASupplier_ClosesTheSuggestions()
    {
        var viewModel = await CreateAsync();
        viewModel.ClearSelectedSupplierForSearch("Kum");
        Assert.True(viewModel.IsSupplierSuggestionsOpen);

        viewModel.SelectSupplierSuggestion(viewModel.SupplierSuggestions.First());

        Assert.False(viewModel.IsSupplierSuggestionsOpen);
        Assert.Equal("Kumar Supplier", viewModel.SelectedSupplier?.Name);
        Assert.Equal("Kumar Supplier", viewModel.SupplierSearchText);
    }

    [Fact]
    public async Task RefocusingAfterSelection_DoesNotReopenTheSuggestions()
    {
        var viewModel = await CreateAsync();
        viewModel.ClearSelectedSupplierForSearch("Kum");
        viewModel.SelectSupplierSuggestion(viewModel.SupplierSuggestions.First());

        viewModel.FocusSupplierSearch();

        Assert.False(viewModel.IsSupplierSuggestionsOpen);
        Assert.Equal("Kumar Supplier", viewModel.SelectedSupplier?.Name);
    }

    [Fact]
    public async Task Escape_ClosesTheSuggestions()
    {
        var viewModel = await CreateAsync();
        viewModel.ClearSelectedSupplierForSearch("Kum");

        viewModel.CloseSupplierSuggestions();

        Assert.False(viewModel.IsSupplierSuggestionsOpen);
    }

    [Fact]
    public async Task Typing_OpensTheSuggestions()
    {
        var viewModel = await CreateAsync();

        viewModel.ClearSelectedSupplierForSearch("Sharma");

        Assert.True(viewModel.IsSupplierSuggestionsOpen);
        Assert.Contains(viewModel.SupplierSuggestions, s => s.Title == "Sharma Distributors");
    }

    [Fact]
    public async Task EmptyText_DoesNotOpenTheSuggestions()
    {
        var viewModel = await CreateAsync();

        viewModel.ClearSelectedSupplierForSearch(string.Empty);

        Assert.False(viewModel.IsSupplierSuggestionsOpen);
    }

    [Fact]
    public async Task TextWithNoMatches_DoesNotOpenTheSuggestions()
    {
        var viewModel = await CreateAsync();

        viewModel.ClearSelectedSupplierForSearch("zzzz-no-such-supplier");

        Assert.False(viewModel.IsSupplierSuggestionsOpen);
    }

    [Fact]
    public async Task SelectedSupplier_SurvivesProductPickerUse()
    {
        var viewModel = await CreateAsync();
        _fixture.AddProduct("Amul Butter 500g", "PRD-000009");
        viewModel.ClearSelectedSupplierForSearch("Kum");
        viewModel.SelectSupplierSuggestion(viewModel.SupplierSuggestions.First());

        // ProductSearchText mirrors the two-way bound TextBox and gates the stale-response check.
        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");
        viewModel.OpenProductPicker();
        viewModel.ToggleProductSelection(viewModel.ProductPickerItems[0]);

        Assert.Equal("Kumar Supplier", viewModel.SelectedSupplier?.Name);
        Assert.False(viewModel.IsSupplierSuggestionsOpen);
        Assert.True(viewModel.IsProductPickerOpen);
    }

    [Fact]
    public async Task SupplierAndProductPopups_AreIndependent()
    {
        var viewModel = await CreateAsync();
        _fixture.AddProduct("Amul Butter 500g", "PRD-000009");

        viewModel.ClearSelectedSupplierForSearch("Kum");
        Assert.True(viewModel.IsSupplierSuggestionsOpen);

        viewModel.OpenProductPicker();
        Assert.True(viewModel.IsProductPickerOpen);
        Assert.True(viewModel.IsSupplierSuggestionsOpen);

        viewModel.CloseSupplierSuggestions();
        Assert.False(viewModel.IsSupplierSuggestionsOpen);
        Assert.True(viewModel.IsProductPickerOpen);

        viewModel.CloseProductPicker();
        Assert.False(viewModel.IsProductPickerOpen);
    }

    [Fact]
    public async Task ClearingTheSupplier_ClosesSuggestionsAndDropsSelection()
    {
        var viewModel = await CreateAsync();
        viewModel.ClearSelectedSupplierForSearch("Kum");
        viewModel.SelectSupplierSuggestion(viewModel.SupplierSuggestions.First());

        viewModel.ClearSupplier();

        Assert.Null(viewModel.SelectedSupplier);
        Assert.Equal(string.Empty, viewModel.SupplierSearchText);
        Assert.False(viewModel.IsSupplierSuggestionsOpen);
    }

    [Fact]
    public async Task RetypingADifferentName_DropsTheCommittedSupplier()
    {
        var viewModel = await CreateAsync();
        viewModel.ClearSelectedSupplierForSearch("Kum");
        viewModel.SelectSupplierSuggestion(viewModel.SupplierSuggestions.First());

        viewModel.ClearSelectedSupplierForSearch("Sharma");

        Assert.Null(viewModel.SelectedSupplier);
        Assert.True(viewModel.IsSupplierSuggestionsOpen);
    }
}
