using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

public interface IBrandService
{
    Task<IReadOnlyList<Brand>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<Brand> CreateAsync(string name, int? performedByUserId, CancellationToken cancellationToken = default);

    Task RenameAsync(int brandId, string newName, int? performedByUserId, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int brandId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);
}
