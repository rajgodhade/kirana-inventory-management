namespace Kirana.Domain.Taxation;

public enum GstinValidationStatus
{
    Missing,
    StructurallyInvalid,
    Valid,
}

public sealed record GstinValidationResult(
    GstinValidationStatus Status,
    string? StateCode = null,
    string? ErrorMessage = null)
{
    public bool IsValid => Status == GstinValidationStatus.Valid;
}

public sealed record GstIdentityValidationResult(
    GstinValidationResult Gstin,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;
}
