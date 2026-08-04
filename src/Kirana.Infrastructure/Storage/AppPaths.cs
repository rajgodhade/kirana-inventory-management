using Kirana.Application.Abstractions;

namespace Kirana.Infrastructure.Storage;

/// <summary>
/// Resolves Kirana's data directory under the current user's local app-data folder
/// (PRD §41): %LOCALAPPDATA%\Kirana\{Data,Backups,Logs,Assets}. Creates the tree on first use.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string AssetsDirectory { get; }
    public string DatabaseFilePath { get; }

    public AppPaths()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kirana");

        DataDirectory = Path.Combine(RootDirectory, "Data");
        BackupsDirectory = Path.Combine(RootDirectory, "Backups");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        AssetsDirectory = Path.Combine(RootDirectory, "Assets");
        DatabaseFilePath = Path.Combine(DataDirectory, "kirana.db");

        foreach (var dir in new[] { DataDirectory, BackupsDirectory, LogsDirectory, AssetsDirectory })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
