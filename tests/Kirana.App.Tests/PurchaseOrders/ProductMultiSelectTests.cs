using Kirana.App.Tests.TestSupport;
using Kirana.App.ViewModels;

namespace Kirana.App.Tests.PurchaseOrders;

/// <summary>
/// Multi-product selection for purchase orders (§5–§13, §23): tick several products across
/// successive searches, then commit them as PO lines in one action.
/// </summary>
public sealed class ProductMultiSelectTests
{
    private readonly PurchaseOrderEntryFixture _fixture = new();

    private async Task<PurchaseOrderEntryViewModel> CreateAsync()
    {
        _fixture.AddSupplier("Kumar Supplier", "SUP-000001");
        _fixture.AddProduct("Amul Butter 500g", "PRD-000009", 52m, "AMUL-BUTTER-500G-013");
        _fixture.AddProduct("Amul Cheese Slices 200g", "PRD-000011", 95m, "AMUL-CHEESE-200G-015");
        _fixture.AddProduct("Tata Salt 1kg", "PRD-000001", 25m, "TATA-SALT-1KG-001");
        _fixture.AddProduct("Parle-G Biscuit 100g", "PRD-000030", 10m, "PARLE-G-100G-030");
        _fixture.AddProduct("5 Star Chocolate Bar 40g", "PRD-000080", 20m, "5-STAR-CHOCOLATE-084");
        var viewModel = _fixture.CreateViewModel();
        await viewModel.InitializeAsync(null);
        return viewModel;
    }

    private static ProductPickerItemViewModel Item(PurchaseOrderEntryViewModel vm, string title) =>
        vm.ProductPickerItems.First(x => x.Title == title);

    /// <summary>
    /// Reproduces the reported screen: a brand new order, picker opened without typing. Nothing is
    /// on the order, so no row may claim "Already added", and every row must carry its name/code.
    /// </summary>
    [Fact]
    public async Task FreshOrder_NoRowIsMarkedAlreadyAdded_AndAllRowsHaveText()
    {
        var viewModel = await CreateAsync();

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        Assert.Empty(viewModel.Lines);
        Assert.NotEmpty(viewModel.ProductPickerItems);
        Assert.All(viewModel.ProductPickerItems, item =>
        {
            Assert.False(item.IsAlreadyAdded);
            Assert.True(item.IsSelectable);
            Assert.False(item.IsSelected);
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Detail));
        });
    }

    /// <summary>Without a replenishment prefill nothing may render as a recommendation.</summary>
    [Fact]
    public async Task FreshOrder_WithoutPrefill_HasNoReplenishmentHighlighting()
    {
        var viewModel = await CreateAsync();

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        Assert.All(viewModel.ProductPickerItems, item =>
        {
            Assert.False(item.IsReplenishmentSuggestion);
            Assert.False(item.ShowGroupDivider);
            Assert.Null(item.GroupHeading);
        });
    }

    [Fact]
    public async Task PickerListsEveryMatch_NotJustTheFirstTwelve()
    {
        _fixture.AddSupplier("Kumar Supplier", "SUP-000001");
        for (var i = 0; i < 40; i++) _fixture.AddProduct($"Bulk Product {i:00}", $"PRD-9{i:0000}");
        var viewModel = _fixture.CreateViewModel();
        await viewModel.InitializeAsync(null);

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        Assert.Equal(40, viewModel.ProductPickerItems.Count);
    }

    /// <summary>
    /// A checkbox click sets IsSelected directly through the two-way binding and never raises the
    /// list's ItemClick. The count must still follow, otherwise the box appears ticked while the
    /// footer reads "None selected" and Add Selected stays disabled.
    /// </summary>
    [Fact]
    public async Task TickingTheCheckBoxDirectly_UpdatesTheCount()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        Item(viewModel, "Tata Salt 1kg").IsSelected = true;

        Assert.Equal(1, viewModel.SelectedProductCount);
        Assert.True(viewModel.HasProductSelection);
        Assert.Equal("Add Selected (1)", viewModel.AddSelectedLabel);
    }

    [Fact]
    public async Task UntickingTheCheckBoxDirectly_UpdatesTheCount()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        var item = Item(viewModel, "Tata Salt 1kg");
        item.IsSelected = true;

        item.IsSelected = false;

        Assert.Equal(0, viewModel.SelectedProductCount);
        Assert.False(viewModel.HasProductSelection);
    }

    [Fact]
    public async Task DirectlyTickedProducts_AreActuallyAdded()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        Item(viewModel, "Tata Salt 1kg").IsSelected = true;
        Item(viewModel, "Parle-G Biscuit 100g").IsSelected = true;

        var added = await viewModel.AddSelectedProductsAsync();

        Assert.Equal(2, added);
        Assert.Equal(2, viewModel.Lines.Count);
    }

    [Fact]
    public async Task SelectingOneProduct_CountsOne()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));

        Assert.Equal(1, viewModel.SelectedProductCount);
        Assert.Equal("Add Selected (1)", viewModel.AddSelectedLabel);
        Assert.True(viewModel.HasProductSelection);
    }

    [Fact]
    public async Task SelectingFourProducts_CountsFour_AndAddsFourLines()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        foreach (var name in new[] { "Amul Butter 500g", "Tata Salt 1kg", "5 Star Chocolate Bar 40g", "Parle-G Biscuit 100g" })
            viewModel.ToggleProductSelection(Item(viewModel, name));

        Assert.Equal(4, viewModel.SelectedProductCount);
        Assert.Equal("Add Selected (4)", viewModel.AddSelectedLabel);

        var added = await viewModel.AddSelectedProductsAsync();

        Assert.Equal(4, added);
        Assert.Equal(4, viewModel.Lines.Count);
        Assert.True(viewModel.HasLines);
    }

    [Fact]
    public async Task SelectionSurvivesChangingTheSearchTerm()
    {
        var viewModel = await CreateAsync();

        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Cheese Slices 200g"));
        Assert.Equal(2, viewModel.SelectedProductCount);

        viewModel.ProductSearchText = "Tata";
        await viewModel.UpdateProductSuggestionsAsync("Tata");
        Assert.Equal(2, viewModel.SelectedProductCount);
        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));

        Assert.Equal(3, viewModel.SelectedProductCount);
        Assert.Equal("Add Selected (3)", viewModel.AddSelectedLabel);

        Assert.Equal(3, await viewModel.AddSelectedProductsAsync());
        Assert.Equal(3, viewModel.Lines.Count);
    }

    [Fact]
    public async Task ReturningToAnEarlierSearch_ShowsThoseItemsStillTicked()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));

        viewModel.ProductSearchText = "Tata";
        await viewModel.UpdateProductSuggestionsAsync("Tata");
        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");

        Assert.True(Item(viewModel, "Amul Butter 500g").IsSelected);
        Assert.False(Item(viewModel, "Amul Cheese Slices 200g").IsSelected);
    }

    [Fact]
    public async Task ClearSelection_ResetsCountAndCheckboxes()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));
        viewModel.ToggleProductSelection(Item(viewModel, "Parle-G Biscuit 100g"));

        viewModel.ClearProductSelection();

        Assert.Equal(0, viewModel.SelectedProductCount);
        Assert.False(viewModel.HasProductSelection);
        Assert.Equal("Add Selected (0)", viewModel.AddSelectedLabel);
        Assert.All(viewModel.ProductPickerItems, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public async Task SelectAll_SelectsOnlyVisibleFilteredProducts()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");

        viewModel.SelectAllVisibleProducts();

        Assert.Equal(2, viewModel.SelectedProductCount);
        Assert.Equal(2, viewModel.ProductPickerItems.Count);
    }

    [Fact]
    public async Task SelectAll_SkipsAlreadyAddedProducts()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));
        await viewModel.AddSelectedProductsAsync();

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.SelectAllVisibleProducts();

        Assert.Equal(4, viewModel.SelectedProductCount);
        Assert.False(Item(viewModel, "Tata Salt 1kg").IsSelected);
    }

    [Fact]
    public async Task AlreadyAddedProduct_IsMarkedAndCannotBeSelected()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));
        await viewModel.AddSelectedProductsAsync();

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        var alreadyAdded = Item(viewModel, "Amul Butter 500g");

        Assert.True(alreadyAdded.IsAlreadyAdded);
        Assert.False(alreadyAdded.IsSelectable);
        Assert.Contains("already added", alreadyAdded.AccessibleName, StringComparison.OrdinalIgnoreCase);

        viewModel.ToggleProductSelection(alreadyAdded);
        Assert.False(alreadyAdded.IsSelected);
        Assert.Equal(0, viewModel.SelectedProductCount);
    }

    [Fact]
    public async Task AlreadyAddedProduct_RefusesDirectSelection()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));
        await viewModel.AddSelectedProductsAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        var alreadyAdded = Item(viewModel, "Amul Butter 500g");
        // Even a direct two-way binding write must not stick.
        alreadyAdded.IsSelected = true;

        Assert.False(alreadyAdded.IsSelected);
    }

    [Fact]
    public async Task AddSelected_ClosesPickerAndResetsSelectionAndSearch()
    {
        var viewModel = await CreateAsync();
        viewModel.OpenProductPicker();
        viewModel.ProductSearchText = "Amul";
        await viewModel.UpdateProductSuggestionsAsync("Amul");
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));

        await viewModel.AddSelectedProductsAsync();

        Assert.False(viewModel.IsProductPickerOpen);
        Assert.Equal(0, viewModel.SelectedProductCount);
        Assert.Equal(string.Empty, viewModel.ProductSearchText);
    }

    [Fact]
    public async Task AddSelected_NeverCreatesDuplicateLines()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));
        await viewModel.AddSelectedProductsAsync();

        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.SelectAllVisibleProducts();
        await viewModel.AddSelectedProductsAsync();

        Assert.Equal(5, viewModel.Lines.Count);
        Assert.Single(viewModel.Lines, line => line.ProductName == "Tata Salt 1kg");
        Assert.Equal(viewModel.Lines.Select(l => l.ProductId).Distinct().Count(), viewModel.Lines.Count);
    }

    [Fact]
    public async Task AddSelected_WithNothingSelected_ReportsAndAddsNothing()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);

        var added = await viewModel.AddSelectedProductsAsync();

        Assert.Equal(0, added);
        Assert.Empty(viewModel.Lines);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task MultiAdd_UsesTheSameDefaultsAsSingleAdd()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Amul Butter 500g"));
        await viewModel.AddSelectedProductsAsync();
        var multi = viewModel.Lines.Single();

        var single = _fixture.CreateViewModel();
        await single.InitializeAsync(null);
        single.ProductSearchText = "Amul Butter 500g";
        await single.AddProductAsync();
        var singleLine = single.Lines.Single();

        Assert.Equal(singleLine.QuantityText, multi.QuantityText);
        Assert.Equal(singleLine.UnitCostText, multi.UnitCostText);
        Assert.Equal(singleLine.DiscountText, multi.DiscountText);
        Assert.Equal("1", multi.QuantityText);
        Assert.Equal("52", multi.UnitCostText);
    }

    [Fact]
    public async Task ExistingSingleProductAdd_StillWorks()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "Tata Salt 1kg";

        await viewModel.AddProductAsync();

        Assert.Single(viewModel.Lines);
        Assert.Equal("Tata Salt 1kg", viewModel.Lines[0].ProductName);
    }

    [Fact]
    public async Task SingleAdd_StillRejectsADuplicate()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "Tata Salt 1kg";
        await viewModel.AddProductAsync();

        viewModel.ProductSearchText = "Tata Salt 1kg";
        await viewModel.AddProductAsync();

        Assert.Single(viewModel.Lines);
        Assert.Contains("already on this order", viewModel.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductNameSearch_StillFilters()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "Parle";

        await viewModel.UpdateProductSuggestionsAsync("Parle");

        Assert.Single(viewModel.ProductPickerItems);
        Assert.Equal("Parle-G Biscuit 100g", viewModel.ProductPickerItems[0].Title);
    }

    [Fact]
    public async Task SkuSearch_StillFilters()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "AMUL-CHEESE-200G-015";

        await viewModel.UpdateProductSuggestionsAsync("AMUL-CHEESE-200G-015");

        Assert.Single(viewModel.ProductPickerItems);
        Assert.Equal("Amul Cheese Slices 200g", viewModel.ProductPickerItems[0].Title);
    }

    [Fact]
    public async Task ProductCodeSearch_StillFilters()
    {
        var viewModel = await CreateAsync();
        viewModel.ProductSearchText = "PRD-000080";

        await viewModel.UpdateProductSuggestionsAsync("PRD-000080");

        Assert.Single(viewModel.ProductPickerItems);
        Assert.Equal("5 Star Chocolate Bar 40g", viewModel.ProductPickerItems[0].Title);
    }

    [Fact]
    public async Task BarcodeSearch_StillResolvesThroughTheSameLookup()
    {
        var viewModel = await CreateAsync();
        var product = _fixture.Products.Items.First(p => p.Name == "Tata Salt 1kg");
        product.Barcodes.Add(new Kirana.Domain.Entities.ProductBarcode
        {
            Value = "8901234567890", NormalizedValue = "8901234567890", IsActive = true,
        });

        viewModel.ProductSearchText = "8901234567890";
        await viewModel.UpdateProductSuggestionsAsync("8901234567890");

        Assert.Single(viewModel.ProductPickerItems);
        Assert.Equal("Tata Salt 1kg", viewModel.ProductPickerItems[0].Title);
    }

    [Fact]
    public async Task RetiredBarcode_IsStillRejected()
    {
        var viewModel = await CreateAsync();
        var product = _fixture.Products.Items.First(p => p.Name == "Tata Salt 1kg");
        product.Barcodes.Add(new Kirana.Domain.Entities.ProductBarcode
        {
            Value = "8909999999999", NormalizedValue = "8909999999999", IsActive = false,
        });

        viewModel.ProductSearchText = "8909999999999";
        await viewModel.UpdateProductSuggestionsAsync("8909999999999");

        Assert.Empty(viewModel.ProductPickerItems);
    }

    [Fact]
    public async Task ClosingThePicker_KeepsTheSelectionForWhenItReopens()
    {
        var viewModel = await CreateAsync();
        await viewModel.UpdateProductSuggestionsAsync(string.Empty);
        viewModel.ToggleProductSelection(Item(viewModel, "Tata Salt 1kg"));

        viewModel.CloseProductPicker();

        Assert.False(viewModel.IsProductPickerOpen);
        Assert.Equal(1, viewModel.SelectedProductCount);
    }
}
