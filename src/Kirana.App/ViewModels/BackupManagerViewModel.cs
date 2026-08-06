using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

public sealed partial class BackupManagerViewModel(
    IBackupService backupService, IKiranaDbContext db, IAppPaths appPaths, ManagementSession session) : ObservableObject
{
    public ObservableCollection<BackupHistoryItemViewModel> History { get; } = [];

    public bool CanManageBackups => session.HasPermission(PermissionKeys.BackupRestore);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _backupLocation = string.Empty;

    [ObservableProperty]
    private string _retentionSummary = string.Empty;

    public bool HasNoBackups => History.Count == 0 && !IsBusy;

    public async Task InitializeAsync()
    {
        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync();

        BackupLocation = string.IsNullOrWhiteSpace(settings?.BackupDirectory)
            ? appPaths.BackupsDirectory
            : settings.BackupDirectory;

        RetentionSummary = settings is null
            ? string.Empty
            : $"Keeping the {settings.BackupRetentionCount} most recent backups · Automatic backup " +
              (settings.AutomaticBackupEnabled ? settings.AutomaticBackupFrequency.ToLowerInvariant() : "off");

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await backupService.GetHistoryAsync();
            History.Clear();
            foreach (var entry in entries)
            {
                History.Add(new BackupHistoryItemViewModel(entry));
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoBackups));
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;

        try
        {
            var result = await backupService.CreateBackupAsync(BackupType.Manual, session.CurrentUser?.Id);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage;
                return;
            }

            StatusMessage = result.DeletedByRetentionCount > 0
                ? $"Backup created and verified. {result.DeletedByRetentionCount} older backup(s) removed by the retention policy."
                : "Backup created and verified.";
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ValidateAsync(BackupHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;

        try
        {
            var validation = await backupService.ValidateBackupAsync(item.FilePath);
            item.ApplyValidation(validation);

            if (validation.IsValid)
            {
                StatusMessage = $"{item.FileName} passed verification.";
            }
            else
            {
                ErrorMessage = $"{item.FileName} failed verification: {string.Join("; ", validation.Errors)}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(BackupHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            await backupService.DeleteBackupAsync(item.Id, session.CurrentUser?.Id);
            StatusMessage = $"{item.FileName} deleted.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            await RefreshAsync();
        }
    }
}

public sealed partial class BackupHistoryItemViewModel : ObservableObject
{
    public BackupHistoryItemViewModel(BackupHistoryEntry entry)
    {
        Id = entry.Record.Id;
        FileName = entry.Record.FileName;
        FilePath = entry.Record.FilePath;
        CreatedAtLocal = entry.Record.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm");
        TypeLabel = entry.Record.BackupType switch
        {
            BackupType.Scheduled => "Automatic",
            BackupType.PreRestoreSafety => "Safety",
            _ => "Manual",
        };
        SizeLabel = FormatBytes(entry.Record.FileSizeBytes);
        FileExists = entry.FileExists;
        _isVerified = entry.Record.IsVerified;
        Notes = entry.Record.Notes;
    }

    public int Id { get; }
    public string FileName { get; }
    public string FilePath { get; }
    public string CreatedAtLocal { get; }
    public string TypeLabel { get; }
    public string SizeLabel { get; }
    public bool FileExists { get; }
    public string? Notes { get; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    /// <summary>A record whose file has been deleted or moved outside Kirana can't be validated or
    /// restored, so the row says so instead of offering actions that would fail.</summary>
    public bool IsMissing => !FileExists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _isVerified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _failedValidation;

    public string StatusLabel => IsMissing
        ? "FILE MISSING"
        : FailedValidation ? "FAILED CHECK" : IsVerified ? "VERIFIED" : "UNVERIFIED";

    public void ApplyValidation(BackupValidationResult validation)
    {
        FailedValidation = !validation.IsValid;
        IsVerified = validation.IsValid;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} bytes",
    };
}
