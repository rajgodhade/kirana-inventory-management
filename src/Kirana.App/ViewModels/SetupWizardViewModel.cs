using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Setup;
using Kirana.Domain.Taxation;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the first-time setup wizard (PRD §5): store profile + GST config + admin account.
/// </summary>
public sealed partial class SetupWizardViewModel(IFirstTimeSetupService setupService) : ObservableObject
{
    [ObservableProperty]
    private string _storeName = string.Empty;

    [ObservableProperty]
    private string _legalName = string.Empty;

    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string? _gstin;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string? _city;

    public IReadOnlyList<IndianGstState> StateOptions { get; } = IndianGstStateCatalog.All;

    [ObservableProperty]
    private IndianGstState? _selectedState;

    public IReadOnlyList<GstRegistrationType> RegistrationTypeOptions { get; } =
        Enum.GetValues<GstRegistrationType>();

    [ObservableProperty]
    private GstRegistrationType? _selectedRegistrationType;

    public string GstinFeedback => BuildGstinFeedback(Gstin, SelectedState?.Code);
    public bool HasGstinFeedback => GstinFeedback.Length > 0;

    [ObservableProperty]
    private string? _pinCode;

    [ObservableProperty]
    private string? _contactNumber;

    [ObservableProperty]
    private string? _email;

    [ObservableProperty]
    private bool _isGstEnabled;

    [ObservableProperty]
    private string _invoicePrefix = "INV";

    [ObservableProperty]
    private string _adminUsername = string.Empty;

    [ObservableProperty]
    private string _adminFullName = string.Empty;

    [ObservableProperty]
    private string _adminPassword = string.Empty;

    [ObservableProperty]
    private string? _adminPin;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSubmitting;

    public event EventHandler? SetupCompleted;

    partial void OnGstinChanged(string? value)
    {
        OnPropertyChanged(nameof(GstinFeedback));
        OnPropertyChanged(nameof(HasGstinFeedback));
    }

    partial void OnSelectedStateChanged(IndianGstState? value)
    {
        OnPropertyChanged(nameof(GstinFeedback));
        OnPropertyChanged(nameof(HasGstinFeedback));
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(StoreName) || string.IsNullOrWhiteSpace(OwnerName))
        {
            ErrorMessage = "Store name and owner name are required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(AdminUsername) || string.IsNullOrWhiteSpace(AdminPassword))
        {
            ErrorMessage = "Admin username and password are required.";
            return;
        }

        IsSubmitting = true;
        try
        {
            await setupService.CompleteSetupAsync(new CompleteSetupRequest
            {
                StoreName = StoreName,
                LegalName = LegalName,
                OwnerName = OwnerName,
                Gstin = Gstin,
                Address = Address,
                City = City,
                State = SelectedState?.Name,
                StateCode = SelectedState?.Code,
                GstRegistrationType = SelectedRegistrationType,
                PinCode = PinCode,
                ContactNumber = ContactNumber,
                Email = Email,
                IsGstEnabled = IsGstEnabled,
                InvoicePrefix = InvoicePrefix,
                AdminUsername = AdminUsername,
                AdminFullName = AdminFullName,
                AdminPassword = AdminPassword,
                AdminPin = string.IsNullOrWhiteSpace(AdminPin) ? null : AdminPin,
            });

            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmitting = false;
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
}
