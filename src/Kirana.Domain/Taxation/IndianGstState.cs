namespace Kirana.Domain.Taxation;

/// <summary>An immutable Indian state or union-territory identity used by GST.</summary>
public sealed record IndianGstState(string Code, string Name)
{
    public string DisplayName => $"{Code} - {Name}";
}
