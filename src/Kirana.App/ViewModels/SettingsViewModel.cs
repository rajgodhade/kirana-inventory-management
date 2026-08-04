using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Settings screen's security section (PRD §8) — currently just the
/// Dashboard inactivity auto-lock duration. <see cref="ManagementSession.AutoLockMinutes"/> is
/// updated in-memory immediately on save so the auto-lock monitor picks up the new value without
/// needing a restart, while the underlying <c>AppSettings</c> row persists it across launches.</summary>
public sealed partial class SettingsViewModel(IKiranaDbContext db, ManagementSession session) : ObservableObject
{
    public bool CanChangeSettings => session.HasPermission(PermissionKeys.SettingsChange);

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

    public async Task InitializeAsync()
    {
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
