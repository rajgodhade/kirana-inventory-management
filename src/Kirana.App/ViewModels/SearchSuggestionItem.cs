using System.Collections.ObjectModel;

namespace Kirana.App.ViewModels;

/// <summary>
/// A small, presentation-only search suggestion. <see cref="Value"/> is the exact text applied
/// to the existing server-side search, so choosing a suggestion never bypasses its normal
/// validation, permissions, or filtering rules.
/// </summary>
public sealed record SearchSuggestionItem(
    string Value,
    string Title,
    string Detail,
    string SearchTerms,
    int? EntityId = null,
    string? GroupHeading = null,
    bool ShowGroupDivider = false,
    bool IsReplenishmentSuggestion = false,
    bool IsAlreadyAdded = false)
{
    public override string ToString() => Title;
}

public static class SearchSuggestionCollection
{
    public static void Update(
        ObservableCollection<SearchSuggestionItem> destination,
        IEnumerable<SearchSuggestionItem> catalog,
        string? query,
        int maximum = 8)
    {
        var term = query?.Trim() ?? string.Empty;
        var matches = catalog
            .Where(item => string.IsNullOrEmpty(term)
                || item.SearchTerms.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => StartsWith(item, term) ? 0 : 1)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(maximum)
            .ToList();

        destination.Clear();
        foreach (var item in matches)
            destination.Add(item);
    }

    private static bool StartsWith(SearchSuggestionItem item, string term) =>
        !string.IsNullOrEmpty(term) && (item.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase)
            || item.Value.StartsWith(term, StringComparison.OrdinalIgnoreCase));
}
