using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

public interface IProductService
{
    Task<Product> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<Product> UpdateAsync(int productId, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int productId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken = default);
}
