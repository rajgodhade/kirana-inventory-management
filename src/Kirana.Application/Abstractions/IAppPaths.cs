namespace Kirana.Application.Abstractions;

/// <summary>
/// Resolves the Windows application-data locations for the store's data (PRD §41):
/// database, backups, logs, and store assets (e.g. logo). Never alongside the .exe.
/// </summary>
public interface IAppPaths
{
    string RootDirectory { get; }
    string DataDirectory { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string AssetsDirectory { get; }
    string DatabaseFilePath { get; }
}
