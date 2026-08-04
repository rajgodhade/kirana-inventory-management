using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

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
            ContactPerson = existingSupplier.ContactPerson ?? string.Empty;
            Phone = existingSupplier.Phone ?? string.Empty;
            Email = existingSupplier.Email ?? string.Empty;
            Address = existingSupplier.Address ?? string.Empty;
        }
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
}
