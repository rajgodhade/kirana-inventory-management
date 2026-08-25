using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Create/Edit Supplier dialog (PRD §28).</summary>
public sealed partial class SupplierEditViewModel : ObservableObject
{
    private readonly SuppliersViewModel _owner;
    private readonly int? _editingSupplierId;

    public bool IsEditMode => _editingSupplierId is not null;

    public string DialogTitle => IsEditMode ? "Edit Supplier" : "Add Supplier";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _gstin = string.Empty;

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
    private string _contactPerson = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public SupplierEditViewModel(SuppliersViewModel owner, Supplier? existingSupplier)
    {
        _owner = owner;

        if (existingSupplier is not null)
        {
            _editingSupplierId = existingSupplier.Id;
            Name = existingSupplier.Name;
            Gstin = existingSupplier.Gstin ?? string.Empty;
            SelectedState = IndianGstStateCatalog.FindByCode(existingSupplier.StateCode);
            SelectedRegistrationType = existingSupplier.GstRegistrationType;
            ContactPerson = existingSupplier.ContactPerson ?? string.Empty;
            Phone = existingSupplier.Phone ?? string.Empty;
            Email = existingSupplier.Email ?? string.Empty;
            Address = existingSupplier.Address ?? string.Empty;
        }
    }

    partial void OnGstinChanged(string value)
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
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        try
        {
            if (IsEditMode)
            {
                await _owner.UpdateSupplierAsync(_editingSupplierId!.Value, new UpdateSupplierRequest
                {
                    Name = Name,
                    Gstin = Gstin,
                    StateCode = SelectedState?.Code,
                    GstRegistrationType = SelectedRegistrationType,
                    ContactPerson = ContactPerson,
                    Phone = Phone,
                    Email = Email,
                    Address = Address,
                    PerformedByUserId = _owner.CurrentUserId,
                });
            }
            else
            {
                await _owner.CreateSupplierAsync(new CreateSupplierRequest
                {
                    Name = Name,
                    Gstin = Gstin,
                    StateCode = SelectedState?.Code,
                    GstRegistrationType = SelectedRegistrationType,
                    ContactPerson = ContactPerson,
                    Phone = Phone,
                    Email = Email,
                    Address = Address,
                    PerformedByUserId = _owner.CurrentUserId,
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string BuildGstinFeedback(string gstin, string? stateCode)
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
