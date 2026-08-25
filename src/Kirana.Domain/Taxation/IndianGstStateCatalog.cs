using System.Collections.ObjectModel;

namespace Kirana.Domain.Taxation;

/// <summary>
/// Authoritative catalog of current Indian GST state and union-territory codes. Persist the
/// two-character <see cref="IndianGstState.Code"/>; names are display text only.
/// </summary>
public static class IndianGstStateCatalog
{
    private static readonly ReadOnlyCollection<IndianGstState> States = Array.AsReadOnly<IndianGstState>(
    [
        new("01", "Jammu and Kashmir"), new("02", "Himachal Pradesh"), new("03", "Punjab"),
        new("04", "Chandigarh"), new("05", "Uttarakhand"), new("06", "Haryana"),
        new("07", "Delhi"), new("08", "Rajasthan"), new("09", "Uttar Pradesh"),
        new("10", "Bihar"), new("11", "Sikkim"), new("12", "Arunachal Pradesh"),
        new("13", "Nagaland"), new("14", "Manipur"), new("15", "Mizoram"),
        new("16", "Tripura"), new("17", "Meghalaya"), new("18", "Assam"),
        new("19", "West Bengal"), new("20", "Jharkhand"), new("21", "Odisha"),
        new("22", "Chhattisgarh"), new("23", "Madhya Pradesh"), new("24", "Gujarat"),
        new("26", "Dadra and Nagar Haveli and Daman and Diu"), new("27", "Maharashtra"),
        new("29", "Karnataka"), new("30", "Goa"), new("31", "Lakshadweep"),
        new("32", "Kerala"), new("33", "Tamil Nadu"), new("34", "Puducherry"),
        new("35", "Andaman and Nicobar Islands"), new("36", "Telangana"),
        new("37", "Andhra Pradesh"), new("38", "Ladakh"),
    ]);

    private static readonly IReadOnlyDictionary<string, IndianGstState> ByCode =
        States.ToDictionary(state => state.Code, StringComparer.Ordinal);

    public static IReadOnlyList<IndianGstState> All => States;

    public static bool IsValidCode(string? code) =>
        code is not null && ByCode.ContainsKey(code.Trim());

    public static IndianGstState? FindByCode(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var state) ? state : null;

    public static IndianGstState GetRequired(string code) => FindByCode(code)
        ?? throw new ArgumentException($"'{code}' is not a supported Indian GST state code.", nameof(code));
}
