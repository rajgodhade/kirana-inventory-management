using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Persistence;

public class EfSequenceGeneratorTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly EfSequenceGenerator _sut;

    public EfSequenceGeneratorTests()
    {
        _sut = new EfSequenceGenerator(_fixture.Context);
    }

    [Fact]
    public async Task NextAsync_StartsAtOne_AndFormatsWithPrefixAndPadding()
    {
        var code = await _sut.NextAsync("Product", "PRD", 6);

        Assert.Equal("PRD-000001", code);
    }

    [Fact]
    public async Task NextAsync_IncrementsOnEachCall()
    {
        var first = await _sut.NextAsync("Product", "PRD", 6);
        var second = await _sut.NextAsync("Product", "PRD", 6);
        var third = await _sut.NextAsync("Product", "PRD", 6);

        Assert.Equal("PRD-000001", first);
        Assert.Equal("PRD-000002", second);
        Assert.Equal("PRD-000003", third);
    }

    [Fact]
    public async Task NextAsync_TracksIndependentSequencesSeparately()
    {
        var productCode = await _sut.NextAsync("Product", "PRD", 6);
        var invoiceCode = await _sut.NextAsync("Invoice", "INV", 6);
        var secondProductCode = await _sut.NextAsync("Product", "PRD", 6);

        Assert.Equal("PRD-000001", productCode);
        Assert.Equal("INV-000001", invoiceCode);
        Assert.Equal("PRD-000002", secondProductCode);
    }

    [Fact]
    public async Task NextNumericAsync_ReturnsRawIncrementingValue()
    {
        var first = await _sut.NextNumericAsync("InternalBarcode");
        var second = await _sut.NextNumericAsync("InternalBarcode");

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task NextAsync_AndNextNumericAsync_ShareTheSameCounterPerKey()
    {
        var formatted = await _sut.NextAsync("Shared", "PRD", 6);
        var raw = await _sut.NextNumericAsync("Shared");

        Assert.Equal("PRD-000001", formatted);
        Assert.Equal(2, raw);
    }

    public void Dispose() => _fixture.Dispose();
}
