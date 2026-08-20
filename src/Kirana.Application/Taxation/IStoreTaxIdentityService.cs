namespace Kirana.Application.Taxation;

public interface IStoreTaxIdentityService
{
    Task<StoreTaxIdentity?> GetAsync(CancellationToken cancellationToken = default);

    Task<StoreTaxIdentity> UpdateAsync(
        UpdateStoreTaxIdentityRequest request,
        CancellationToken cancellationToken = default);
}
