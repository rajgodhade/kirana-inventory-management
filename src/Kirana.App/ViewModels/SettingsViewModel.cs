using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.CloudBackup;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Settings screen's security section (PRD §8) — currently just the
/// Dashboard inactivity auto-lock duration. <see cref="ManagementSession.AutoLockMinutes"/> is
/// updated in-memory immediately on save so the auto-lock monitor picks up the new value without
/// needing a restart, while the underlying <c>AppSettings</c> row persists it across launches.</summary>
public sealed partial class SettingsViewModel(
    IKiranaDbContext db, IAppPaths appPaths, ManagementSession session, ICloudBackupService cloudBackupService,
    IStoreTaxIdentityService storeTaxIdentityService) : ObservableObject
{
    public bool CanChangeSettings => session.HasPermission(PermissionKeys.SettingsChange);

    public IReadOnlyList<IndianGstState> StateOptions { get; } = IndianGstStateCatalog.All;
    public IReadOnlyList<GstRegistrationType> RegistrationTypeOptions { get; } =
        Enum.GetValues<GstRegistrationType>();

    [ObservableProperty] private string _storeTradeName = string.Empty;
    [ObservableProperty] private string _storeLegalName = string.Empty;
    [ObservableProperty] private string _storeGstin = string.Empty;
    [ObservableProperty] private IndianGstState? _selectedStoreState;
    [ObservableProperty] private GstRegistrationType? _selectedStoreRegistrationType;
    [ObservableProperty] private string? _storeTaxIdentityErrorMessage;
    [ObservableProperty] private string? _storeTaxIdentityStatusMessage;

    public string StoreGstinFeedback => BuildGstinFeedback(StoreGstin, SelectedStoreState?.Code);
    public bool HasStoreGstinFeedback => StoreGstinFeedback.Length > 0;

    partial void OnStoreGstinChanged(string value)
    {
        OnPropertyChanged(nameof(StoreGstinFeedback));
        OnPropertyChanged(nameof(HasStoreGstinFeedback));
    }

    partial void OnSelectedStoreStateChanged(IndianGstState? value)
    {
        OnPropertyChanged(nameof(StoreGstinFeedback));
        OnPropertyChanged(nameof(HasStoreGstinFeedback));
    }

    public IReadOnlyList<string> DurationOptions { get; } = ["5 minutes", "10 minutes", "15 minutes", "30 minutes", "Custom"];

    [ObservableProperty]
    private string _selectedDurationOption = "10 minutes";

    [ObservableProperty]
    private string _customMinutesText = "10";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsCustomSelected => SelectedDurationOption == "Custom";

    partial void OnSelectedDurationOptionChanged(string value) => OnPropertyChanged(nameof(IsCustomSelected));

    [ObservableProperty]
    private bool _requirePinForPriceOverride = true;

    [ObservableProperty]
    private bool _requirePinForLargeDiscount = true;

    [ObservableProperty]
    private bool _requirePinForReprint = true;

    [ObservableProperty]
    private string? _pinToggleErrorMessage;

    [ObservableProperty]
    private string? _pinToggleStatusMessage;

    /// <summary>Guards the three PIN toggles' change handlers against firing (and re-saving) while
    /// <see cref="InitializeAsync"/> is setting their initial value.</summary>
    private bool _isLoadingPinToggles;

    // ---------------------------------------------------------------- Backup & export (Phase 11)

    public IReadOnlyList<string> BackupFrequencyOptions { get; } = ["Daily", "Weekly"];

    public IReadOnlyList<string> ExportFormatOptions { get; } = ["Csv", "Excel"];

    [ObservableProperty]
    private bool _automaticBackupEnabled = true;

    [ObservableProperty]
    private string _automaticBackupFrequency = "Daily";

    [ObservableProperty]
    private string _backupDirectory = string.Empty;

    [ObservableProperty]
    private string _backupRetentionText = "14";

    [ObservableProperty]
    private string _defaultExportFormat = "Csv";

    [ObservableProperty]
    private string? _backupSettingsErrorMessage;

    [ObservableProperty]
    private string? _backupSettingsStatusMessage;

    public IReadOnlyList<string> CloudProviderOptions { get; } = ["Not connected", "Google Drive", "OneDrive"];
    [ObservableProperty] private string _cloudProvider = "Not connected";
    [ObservableProperty] private bool _cloudAutomaticBackupEnabled;
    [ObservableProperty] private string _cloudBackupFrequency = "Daily";
    [ObservableProperty] private string _cloudBackupTime = "23:00";
    [ObservableProperty] private string _cloudBackupRetentionText = "30";
    [ObservableProperty] private string? _cloudBackupAccount;
    [ObservableProperty] private string _lastCloudBackupText = "No cloud backup yet";
    [ObservableProperty] private string? _cloudBackupStatusMessage;
    [ObservableProperty] private string? _cloudBackupErrorMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudBackups))]
    [NotifyPropertyChangedFor(nameof(HasNoCloudBackups))]
    [NotifyPropertyChangedFor(nameof(CloudBackupCountText))]
    private IReadOnlyList<CloudBackupListItem> _cloudBackups = [];
    [ObservableProperty] private bool _showCloudBackups;
    [ObservableProperty] private bool _isCloudBackupListLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCloudBackup))]
    [NotifyPropertyChangedFor(nameof(CanManageCloudConnection))]
    [NotifyPropertyChangedFor(nameof(CanViewCloudBackups))]
    [NotifyPropertyChangedFor(nameof(CloudBackupButtonText))]
    private bool _isCloudBackupRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudIsDisconnected))]
    [NotifyPropertyChangedFor(nameof(CanStartCloudBackup))]
    [NotifyPropertyChangedFor(nameof(CanViewCloudBackups))]
    private bool _cloudIsConnected;
    public bool CloudIsDisconnected => !CloudIsConnected;
    public bool CanStartCloudBackup => CloudIsConnected && !IsCloudBackupRunning;
    public bool CanManageCloudConnection => CanChangeSettings && !IsCloudBackupRunning;
    public bool CanViewCloudBackups => CloudIsConnected && !IsCloudBackupRunning;
    public string CloudBackupButtonText => IsCloudBackupRunning ? "Uploading..." : "Backup now";
    public bool HasCloudBackups => CloudBackups.Count > 0;
    public bool HasNoCloudBackups => ShowCloudBackups && !IsCloudBackupListLoading && CloudBackups.Count == 0;
    public string CloudBackupCountText => CloudBackups.Count == 1 ? "1 backup" : $"{CloudBackups.Count} backups";
    public string CloudAccountDisplay => string.IsNullOrWhiteSpace(CloudBackupAccount)
        ? "Reconnect once to display the account email"
        : $"Connected account: {CloudBackupAccount}";

    private bool _isLoadingBackupSettings;

    [RelayCommand]
    private async Task ConnectCloudAsync()
    {
        CloudBackupErrorMessage = null;
        var kind = CloudProvider switch { "Google Drive" => CloudBackupProviderKind.GoogleDrive, "OneDrive" => CloudBackupProviderKind.OneDrive, _ => CloudBackupProviderKind.None };
        if (kind == CloudBackupProviderKind.None) { CloudBackupErrorMessage = "Choose Google Drive or OneDrive first."; return; }
        var result = await cloudBackupService.ConnectAsync(kind);
        if (!result.Succeeded) { CloudBackupErrorMessage = result.ErrorMessage; return; }
        await RefreshCloudAsync();
        CloudBackupStatusMessage = "Google Drive connected successfully.";
    }

    [RelayCommand]
    private async Task DisconnectCloudAsync()
    {
        await cloudBackupService.DisconnectAsync();
        CloudProvider = "Not connected";
        CloudBackupAccount = null;
        CloudIsConnected = false;
        CloudBackupStatusMessage = "Cloud backup disconnected.";
    }

    [RelayCommand]
    private async Task BackupToCloudAsync()
    {
        if (IsCloudBackupRunning) return;

        CloudBackupErrorMessage = null;
        CloudBackupStatusMessage = null;
        IsCloudBackupRunning = true;
        try
        {
            var result = await cloudBackupService.BackupNowAsync(session.CurrentUser?.Id);
            if (!result.Succeeded) { CloudBackupErrorMessage = result.ErrorMessage; return; }
            await RefreshCloudAsync();
            CloudBackupStatusMessage = "Cloud backup completed successfully.";
        }
        finally
        {
            IsCloudBackupRunning = false;
        }
    }

    [RelayCommand]
    private async Task ViewCloudBackupsAsync()
    {
        CloudBackupErrorMessage = null;
        CloudBackupStatusMessage = null;
        ShowCloudBackups = true;
        IsCloudBackupListLoading = true;
        OnPropertyChanged(nameof(HasNoCloudBackups));
        try
        {
            var backups = await cloudBackupService.ListBackupsAsync();
            CloudBackups = backups.Select(backup => new CloudBackupListItem(
                backup.FileName,
                backup.CreatedAtUtc == DateTime.MinValue ? "Upload time unavailable" : backup.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"),
                FormatFileSize(backup.SizeBytes))).ToList();
        }
        finally
        {
            IsCloudBackupListLoading = false;
            OnPropertyChanged(nameof(HasNoCloudBackups));
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1024L * 1024L => $"{bytes / (1024d * 1024d):0.0} MB",
        >= 1024L => $"{bytes / 1024d:0.0} KB",
        > 0 => $"{bytes} bytes",
        _ => "Size unavailable",
    };

    public async Task RefreshCloudAsync()
    {
        var settings = await db.AppSettings.FirstOrDefaultAsync();
        CloudProvider = settings?.CloudBackupProvider switch { "GoogleDrive" => "Google Drive", "OneDrive" => "OneDrive", _ => "Not connected" };
        CloudAutomaticBackupEnabled = settings?.CloudAutomaticBackupEnabled ?? false;
        CloudBackupFrequency = settings?.CloudBackupFrequency ?? "Daily";
        CloudBackupTime = settings?.CloudBackupTime ?? "23:00";
        CloudBackupRetentionText = (settings?.CloudBackupRetentionCount ?? 30).ToString();
        LastCloudBackupText = settings?.LastCloudBackupUtc is { } last
            ? $"Last backup: {last.ToLocalTime():dd MMM yyyy, hh:mm tt}"
            : "No cloud backup yet";
        CloudIsConnected = await cloudBackupService.IsConnectedAsync();
        CloudBackupAccount = CloudIsConnected && await cloudBackupService.GetAccountInfoAsync() is { } account ? account.Email : null;
        OnPropertyChanged(nameof(CloudAccountDisplay));
    }

    partial void OnAutomaticBackupEnabledChanged(bool value) =>
        _ = SaveBackupSettingAsync(s => s.AutomaticBackupEnabled = value);

    partial void OnAutomaticBackupFrequencyChanged(string value) =>
        _ = SaveBackupSettingAsync(s => s.AutomaticBackupFrequency = value);

    partial void OnDefaultExportFormatChanged(string value) =>
        _ = SaveBackupSettingAsync(s => s.DefaultExportFormat = value);

    partial void OnCloudAutomaticBackupEnabledChanged(bool value) =>
        _ = SaveBackupSettingAsync(s => s.CloudAutomaticBackupEnabled = value);

    partial void OnCloudBackupFrequencyChanged(string value) =>
        _ = SaveBackupSettingAsync(s => s.CloudBackupFrequency = value);

    partial void OnCloudBackupTimeChanged(string value) =>
        _ = SaveBackupSettingAsync(s => s.CloudBackupTime = value);

    /// <summary>Called by the page after the folder picker returns, and by the Save button next to
    /// the retention field — both write through the same immediate-save path as the toggles.</summary>
    public Task SaveBackupDirectoryAsync(string directory)
    {
        BackupDirectory = directory;
        return SaveBackupSettingAsync(s => s.BackupDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory);
    }

    [RelayCommand]
    private async Task SaveRetentionAsync()
    {
        if (!int.TryParse(BackupRetentionText, out var retention) || retention < 1)
        {
            BackupSettingsErrorMessage = "Keep at least 1 backup.";
            return;
        }

        await SaveBackupSettingAsync(s => s.BackupRetentionCount = retention, force: true);
    }

    [RelayCommand]
    private Task UseDefaultBackupFolderAsync() => SaveBackupDirectoryAsync(string.Empty);

    /// <summary>Same immediate-save shape as <see cref="SavePinToggleAsync"/>. The backup scheduler
    /// re-reads <c>AppSettings</c> on every run, so unlike the auto-lock and PIN settings there is
    /// nothing to mirror into <see cref="ManagementSession"/>.</summary>
    private async Task SaveBackupSettingAsync(Action<AppSettings> apply, bool force = false)
    {
        if ((_isLoadingBackupSettings && !force) || !CanChangeSettings)
        {
            return;
        }

        BackupSettingsErrorMessage = null;
        BackupSettingsStatusMessage = null;

        var settings = await db.AppSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            BackupSettingsErrorMessage = "Application settings are not initialized.";
            return;
        }

        apply(settings);
        await db.SaveChangesAsync();
        BackupSettingsStatusMessage = "Saved.";
    }

    public async Task InitializeAsync()
    {
        var storeIdentity = await storeTaxIdentityService.GetAsync();
        if (storeIdentity is not null)
        {
            StoreTradeName = storeIdentity.TradeName;
            StoreLegalName = storeIdentity.LegalName ?? string.Empty;
            StoreGstin = storeIdentity.Gstin ?? string.Empty;
            SelectedStoreState = IndianGstStateCatalog.FindByCode(storeIdentity.StateCode);
            SelectedStoreRegistrationType = storeIdentity.RegistrationType;
        }

        var settings = await db.AppSettings.FirstOrDefaultAsync();
        var currentMinutes = settings?.AutoLockMinutes ?? session.AutoLockMinutes;

        SelectedDurationOption = currentMinutes switch
        {
            5 => "5 minutes",
            10 => "10 minutes",
            15 => "15 minutes",
            30 => "30 minutes",
            _ => "Custom",
        };
        CustomMinutesText = currentMinutes.ToString();

        _isLoadingPinToggles = true;
        RequirePinForPriceOverride = settings?.RequirePinForPriceOverride ?? session.RequirePinForPriceOverride;
        RequirePinForLargeDiscount = settings?.RequirePinForLargeDiscount ?? session.RequirePinForLargeDiscount;
        RequirePinForReprint = settings?.RequirePinForReprint ?? session.RequirePinForReprint;
        _isLoadingPinToggles = false;

        _isLoadingBackupSettings = true;
        AutomaticBackupEnabled = settings?.AutomaticBackupEnabled ?? true;
        AutomaticBackupFrequency = settings?.AutomaticBackupFrequency ?? "Daily";
        BackupDirectory = settings?.BackupDirectory ?? string.Empty;
        BackupRetentionText = (settings?.BackupRetentionCount ?? 14).ToString();
        DefaultExportFormat = settings?.DefaultExportFormat ?? "Csv";
        await RefreshCloudAsync();
        _isLoadingBackupSettings = false;
    }

    [RelayCommand]
    private async Task SaveStoreTaxIdentityAsync()
    {
        StoreTaxIdentityErrorMessage = null;
        StoreTaxIdentityStatusMessage = null;
        try
        {
            await storeTaxIdentityService.UpdateAsync(new UpdateStoreTaxIdentityRequest
            {
                TradeName = StoreTradeName,
                LegalName = StoreLegalName,
                Gstin = StoreGstin,
                StateCode = SelectedStoreState?.Code,
                RegistrationType = SelectedStoreRegistrationType,
                PerformedByUserId = session.CurrentUser?.Id,
            });
            StoreTaxIdentityStatusMessage = "Store GST identity saved.";
        }
        catch (Exception ex)
        {
            StoreTaxIdentityErrorMessage = ex.Message;
        }
    }

    private static string BuildGstinFeedback(string? gstin, string? stateCode)
    {
        var result = GstinValidator.ValidateIdentity(gstin, stateCode);
        return result.Gstin.Status switch
        {
            GstinValidationStatus.Missing => string.Empty,
            _ when !result.IsValid => result.ErrorMessage ?? "GSTIN is invalid.",
            _ => $"Valid GSTIN for {IndianGstStateCatalog.FindByCode(result.Gstin.StateCode)?.Name}.",
        };
    }

    /// <summary>Shown when no explicit folder is configured, so the operator can see where backups
    /// actually land rather than an empty box.</summary>
    public string BackupDirectoryPlaceholder => appPaths.BackupsDirectory;

    partial void OnRequirePinForPriceOverrideChanged(bool value) =>
        _ = SavePinToggleAsync(value, (s, v) => s.RequirePinForPriceOverride = v, () => session.RequirePinForPriceOverride = value);

    partial void OnRequirePinForLargeDiscountChanged(bool value) =>
        _ = SavePinToggleAsync(value, (s, v) => s.RequirePinForLargeDiscount = v, () => session.RequirePinForLargeDiscount = value);

    partial void OnRequirePinForReprintChanged(bool value) =>
        _ = SavePinToggleAsync(value, (s, v) => s.RequirePinForReprint = v, () => session.RequirePinForReprint = value);

    /// <summary>Persists one PIN toggle immediately (no separate Save button, matching the
    /// Appearance section's pattern) and mirrors it into <see cref="ManagementSession"/> so POS and
    /// Management Home — both of which run without an unlocked session and can't re-read AppSettings
    /// on every action — pick up the change without an app restart.</summary>
    private async Task SavePinToggleAsync(bool value, Action<AppSettings, bool> apply, Action applyToSession)
    {
        if (_isLoadingPinToggles || !CanChangeSettings)
        {
            return;
        }

        PinToggleErrorMessage = null;
        PinToggleStatusMessage = null;

        var settings = await db.AppSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            PinToggleErrorMessage = "Application settings are not initialized.";
            return;
        }

        apply(settings, value);
        await db.SaveChangesAsync();
        applyToSession();

        PinToggleStatusMessage = "Saved.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!TryResolveMinutes(out var minutes, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var settings = await db.AppSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            ErrorMessage = "Application settings are not initialized.";
            return;
        }

        settings.AutoLockMinutes = minutes;
        await db.SaveChangesAsync();

        session.AutoLockMinutes = minutes;
        StatusMessage = $"Auto-lock set to {minutes} minute(s).";
    }

    private bool TryResolveMinutes(out int minutes, out string? error)
    {
        if (!IsCustomSelected)
        {
            minutes = int.Parse(SelectedDurationOption.Split(' ')[0]);
            error = null;
            return true;
        }

        if (!int.TryParse(CustomMinutesText, out minutes) || minutes is < 1 or > 1440)
        {
            error = "Custom duration must be a whole number of minutes between 1 and 1440 (24 hours).";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed record CloudBackupListItem(string FileName, string UploadedAtText, string SizeText);
