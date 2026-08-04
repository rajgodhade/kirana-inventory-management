namespace Kirana.Application.Setup;

/// <summary>
/// Drives the first-launch flow (PRD §5): before setup is completed, the app must show
/// the setup wizard instead of POS; afterwards it always launches straight into POS (§4).
/// </summary>
public interface IFirstTimeSetupService
{
    Task<bool> IsSetupCompletedAsync(CancellationToken cancellationToken = default);

    Task CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default);
}
