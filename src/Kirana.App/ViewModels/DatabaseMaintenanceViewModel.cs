using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Maintenance;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class DatabaseMaintenanceViewModel(
    IDatabaseMaintenanceService maintenanceService,
    IBackupService backupService,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<string> TableStatistics { get; } = [];

    public ObservableCollection<OrphanRecordGroup> OrphanGroups { get; } = [];

    public bool CanRunMaintenance => session.HasPermission(PermissionKeys.BackupRestore);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _databaseLocation = string.Empty;

    [ObservableProperty]
    private string _sizeSummary = string.Empty;

    [ObservableProperty]
    private string _maintenanceSummary = string.Empty;

    [ObservableProperty]
    private bool _hasCheckedOrphans;

    public bool HasOrphans => OrphanGroups.Any(g => g.Count > 0);

    public async Task InitializeAsync() => await RefreshStatisticsAsync();

    [RelayCommand]
    private async Task RefreshStatisticsAsync()
    {
        if (!CanRunMaintenance)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var stats = await maintenanceService.GetStatisticsAsync(session.CurrentUser?.Id);

            DatabaseLocation = stats.DatabaseFilePath;
            SizeSummary = $"{FormatBytes(stats.FileSizeBytes)} on disk · {stats.PageCount:N0} pages of " +
                          $"{stats.PageSize:N0} bytes · {FormatBytes(stats.ReclaimableBytes)} reclaimable by a vacuum";
            MaintenanceSummary =
                $"Last backup: {FormatTimestamp(stats.LastBackupUtc)} · " +
                $"Last vacuum: {FormatTimestamp(stats.LastVacuumUtc)} · " +
                $"Last analyze: {FormatTimestamp(stats.LastAnalyzeUtc)}";

            TableStatistics.Clear();
            foreach (var (table, count) in stats.TableRowCounts)
            {
                TableStatistics.Add($"{table}: {count:N0}");
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

    [RelayCommand]
    private async Task RunIntegrityCheckAsync()
    {
        await RunAsync(async () =>
        {
            var result = await maintenanceService.RunIntegrityCheckAsync(session.CurrentUser?.Id);
            if (result.IsHealthy)
            {
                StatusMessage = "Integrity check passed — the database reports no problems.";
            }
            else
            {
                ErrorMessage = "Integrity check found problems: " + string.Join("; ", result.Messages);
            }
        });
    }

    [RelayCommand]
    private async Task VacuumAsync()
    {
        await RunAsync(async () =>
        {
            var result = await maintenanceService.VacuumAsync(session.CurrentUser?.Id);
            if (result.Succeeded)
            {
                StatusMessage = "Vacuum complete. " + result.Details;
                await RefreshStatisticsAsync();
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
            }
        });
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        await RunAsync(async () =>
        {
            var result = await maintenanceService.AnalyzeAsync(session.CurrentUser?.Id);
            if (result.Succeeded)
            {
                StatusMessage = "Analyze complete — query planner statistics refreshed.";
                await RefreshStatisticsAsync();
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
            }
        });
    }

    [RelayCommand]
    private async Task FindOrphansAsync()
    {
        await RunAsync(async () =>
        {
            var groups = await maintenanceService.FindOrphanRecordsAsync(session.CurrentUser?.Id);

            OrphanGroups.Clear();
            foreach (var group in groups)
            {
                OrphanGroups.Add(group);
            }

            HasCheckedOrphans = true;
            OnPropertyChanged(nameof(HasOrphans));

            StatusMessage = HasOrphans
                ? "Dangling references found — listed below for review. Nothing was changed."
                : "No dangling references found.";
        });
    }

    /// <summary>Verifies an arbitrary backup file the operator points at, using the same validation
    /// a restore would run first.</summary>
    public async Task VerifyBackupAsync(string filePath)
    {
        await RunAsync(async () =>
        {
            var validation = await backupService.ValidateBackupAsync(filePath);
            if (validation.IsValid)
            {
                StatusMessage = $"{Path.GetFileName(filePath)} is a valid backup " +
                                $"(taken {validation.Manifest!.CreatedAtUtc.ToLocalTime():dd MMM yyyy, HH:mm}).";
            }
            else
            {
                ErrorMessage = $"{Path.GetFileName(filePath)} failed verification: {string.Join("; ", validation.Errors)}";
            }
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;

        try
        {
            await operation();
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

    private static string FormatTimestamp(DateTime? utc) =>
        utc is { } value ? value.ToLocalTime().ToString("dd MMM yyyy, HH:mm") : "never";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} bytes",
    };
}
