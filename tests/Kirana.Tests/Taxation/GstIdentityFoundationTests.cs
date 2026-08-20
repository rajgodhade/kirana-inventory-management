using Kirana.Domain.Taxation;

namespace Kirana.Tests.Taxation;

public sealed class GstIdentityFoundationTests
{
    [Fact]
    public void State_catalog_contains_every_current_state_and_union_territory_once()
    {
        Assert.Equal(36, IndianGstStateCatalog.All.Count);
        Assert.Equal(36, IndianGstStateCatalog.All.Select(state => state.Code).Distinct().Count());
        Assert.All(IndianGstStateCatalog.All, state =>
        {
            Assert.Matches("^[0-9]{2}$", state.Code);
            Assert.False(string.IsNullOrWhiteSpace(state.Name));
        });
    }

    [Theory]
    [InlineData("27", "Maharashtra")]
    [InlineData("07", "Delhi")]
    [InlineData("38", "Ladakh")]
    public void State_lookup_is_stable(string code, string expectedName)
    {
        var first = IndianGstStateCatalog.GetRequired(code);
        var second = IndianGstStateCatalog.GetRequired(code);
        Assert.Same(first, second);
        Assert.Equal(expectedName, first.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("28")]
    [InlineData("MH")]
    [InlineData("99")]
    public void Invalid_state_codes_are_rejected(string? code)
    {
        Assert.False(IndianGstStateCatalog.IsValidCode(code));
        Assert.Null(IndianGstStateCatalog.FindByCode(code));
    }

    [Fact]
    public void Registration_model_contains_only_current_foundation_categories()
    {
        Assert.Equal(
            [GstRegistrationType.Regular, GstRegistrationType.Composition, GstRegistrationType.Unregistered],
            Enum.GetValues<GstRegistrationType>());
    }

    [Fact]
    public void Missing_gstin_is_distinct_from_invalid_gstin()
    {
        Assert.Equal(GstinValidationStatus.Missing, GstinValidator.Validate(null).Status);
        Assert.Equal(GstinValidationStatus.Missing, GstinValidator.Validate("  ").Status);
        Assert.Equal(GstinValidationStatus.StructurallyInvalid, GstinValidator.Validate("invalid").Status);
    }

    [Theory]
    [InlineData("27AAPFU0939F1ZV", "27")]
    [InlineData("29GGGGG1314R9ZA", "29")]
    public void Valid_gstin_passes_checksum_and_exposes_state(string gstin, string stateCode)
    {
        var result = GstinValidator.Validate(gstin);
        Assert.True(result.IsValid);
        Assert.Equal(stateCode, result.StateCode);
    }

    [Theory]
    [InlineData("27AAPFU0939F1ZU")]
    [InlineData("27aapfu0939f1zv")]
    [InlineData("99AAPFU0939F1ZV")]
    [InlineData("27AAPFU0939F1Z")]
    [InlineData("27AAPF!0939F1ZV")]
    public void Invalid_gstin_is_rejected(string gstin) =>
        Assert.Equal(GstinValidationStatus.StructurallyInvalid, GstinValidator.Validate(gstin).Status);

    [Fact]
    public void Matching_gstin_and_state_is_valid()
    {
        var result = GstinValidator.ValidateIdentity("27AAPFU0939F1ZV", "27");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Gstin_state_mismatch_is_rejected_without_overwriting_selected_state()
    {
        const string selectedState = "29";
        var result = GstinValidator.ValidateIdentity("27AAPFU0939F1ZV", selectedState);
        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.ErrorMessage);
        Assert.Equal("29", selectedState);
    }

    [Fact]
    public void State_can_be_configured_without_gstin()
    {
        var result = GstinValidator.ValidateIdentity(null, "27");
        Assert.True(result.IsValid);
        Assert.Equal(GstinValidationStatus.Missing, result.Gstin.Status);
    }
}
