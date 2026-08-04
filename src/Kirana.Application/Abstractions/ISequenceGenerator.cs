namespace Kirana.Application.Abstractions;

/// <summary>
/// Atomically issues the next number in a named sequence and formats it as a stable,
/// human-readable code (e.g. "PRD-000001"). Backed by a dedicated counter row per
/// sequence (PRD §11, §22) rather than MAX(id)+1, which is unsafe under concurrent callers.
/// </summary>
public interface ISequenceGenerator
{
    Task<string> NextAsync(string sequenceKey, string prefix, int padding, CancellationToken cancellationToken = default);

    /// <summary>Same atomic counter, without prefix/padding formatting — for callers that need
    /// the raw number (e.g. building a numeric-only EAN-13 internal barcode).</summary>
    Task<long> NextNumericAsync(string sequenceKey, CancellationToken cancellationToken = default);
}
