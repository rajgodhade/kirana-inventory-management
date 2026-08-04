namespace Kirana.App.ViewModels;

/// <summary>Flattened, read-only row for the Audit Log screen (PRD §37). No <c>required</c>
/// members — see the Phase 5 note on avoiding required members on types reachable from a bound
/// ViewModel.</summary>
public sealed class AuditLogRowViewModel
{
    public string TimestampText { get; init; } = "";
    public string UserDisplay { get; init; } = "System";
    public string Action { get; init; } = "";
    public string Entity { get; init; } = "";
    public string? EntityId { get; init; }
    public string? Details { get; init; }
}
