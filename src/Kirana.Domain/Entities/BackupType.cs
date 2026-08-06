namespace Kirana.Domain.Entities;

/// <summary>Distinguishes how a <see cref="BackupRecord"/> was produced (PRD §11/§38).</summary>
public enum BackupType
{
    Manual,
    Scheduled,

    /// <summary>Taken automatically by <c>RestoreService</c> immediately before overwriting the
    /// live database, so a bad restore can always be undone.</summary>
    PreRestoreSafety,
}
