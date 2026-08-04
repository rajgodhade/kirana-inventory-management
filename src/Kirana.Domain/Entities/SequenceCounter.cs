namespace Kirana.Domain.Entities;

/// <summary>
/// Backs atomic generation of stable human-readable codes (e.g. "PRD-000001",
/// "INV-2026-000001" in later phases). One row per named sequence; never read/write
/// <see cref="NextValue"/> directly — go through ISequenceGenerator so increments stay
/// race-free across concurrent callers.
/// </summary>
public class SequenceCounter
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int NextValue { get; set; } = 1;
}
