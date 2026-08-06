using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Restore;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class RestoreViewModel(
    IRestoreService restoreService, IBackupService backupService, ManagementSession session) : ObservableObject
{
    public ObservableCollection<BackupHistoryItemViewModel> AvailableBackups { get; } = [];

    public ObservableCollection<string> SelectedBackupContents { get; } = [];

    public bool CanRestore => session.HasPermission(PermissionKeys.BackupRestore);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBackup))]
    private string? _selectedBackupPath;

    [ObservableProperty]
    private string? _selectedBackupSummary;

    /// <summary>Set once a restore has succeeded — the page then shows only the restart prompt,
    /// because everything else on screen is reading from a database that no longer exists.</summary>
    [ObservableProperty]
    private bool _restoreCompleted;

    public bool HasSelectedBackup => !string.IsNullOrEmpty(SelectedBackupPath);

    public bool HasNoBackups => AvailableBackups.Count == 0 && !IsBusy;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            AvailableBackups.Clear();
            foreach (var entry in await backupService.GetHistoryAsync())
            {
                if (entry.FileExists)
                {
                    AvailableBackups.Add(new BackupHistoryItemViewModel(entry));
                }
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoBackups));
        }
    }

    /// <summary>
    /// Validates and describes a candidate bundle. Nothing here touches the live database — this is
    /// the "show the operator what they are about to overwrite with" step.
    /// </summary>
    public async Task InspectAsync(string filePath)
    {
        ErrorMessage = null;
        StatusMessage = null;
        SelectedBackupPath = null;
        SelectedBackupSummary = null;
        SelectedBackupContents.Clear();
        IsBusy = true;

        try
        {
            var info = await restoreService.GetBackupInfoAsync(filePath);

            SelectedBackupPath = filePath;
            SelectedBackupSummary =
                $"Taken {info.Manifest.CreatedAtUtc.ToLocalTime():dd MMM yyyy, HH:mm} · " +
                $"{info.Manifest.BackupType} · {FormatBytes(info.FileSizeBytes)}" +
                (info.Manifest.StoreName is { } store ? $" · {store}" : string.Empty);

            foreach (var (table, count) in info.RowCounts)
            {
                SelectedBackupContents.Add(count < 0 ? $"{table}: not present in this backup" : $"{table}: {count:N0}");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the restore. The caller is responsible for having confirmed with the operator
    /// first — by the time this is called the decision is already made.</summary>
    public async Task<bool> RestoreAsync(int authorizedByUserId)
    {
        if (SelectedBackupPath is null)
        {
            return false;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;

        try
        {
            var result = await restoreService.RestoreAsync(SelectedBackupPath, authorizedByUserId);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage;
                return false;
            }

            RestoreCompleted = true;
            StatusMessage = result.Warnings.Count == 0
                ? $"Restore complete. Your previous data was saved to {Path.GetFileName(result.SafetyBackupPath)} before it was replaced."
                : $"Restore complete, with warnings: {string.Join("; ", result.Warnings)}. " +
                  $"Your previous data was saved to {Path.GetFileName(result.SafetyBackupPath)}.";

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} bytes",
    };
}
