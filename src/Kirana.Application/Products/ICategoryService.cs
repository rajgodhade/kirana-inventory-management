using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<Category> CreateAsync(string name, int? performedByUserId, CancellationToken cancellationToken = default);

    Task RenameAsync(int categoryId, string newName, int? performedByUserId, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int categoryId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);
}
