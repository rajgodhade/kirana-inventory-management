using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>
/// What a staged price write actually changed, so the caller can audit it after its own
/// SaveChanges. <paramref name="PreviousPrice"/> is null when the level was not configured before,
/// and <paramref name="NewPrice"/> is null when the level has just been withdrawn.
/// </summary>
public sealed record PriceChange(PriceLevel Level, decimal? PreviousPrice, decimal? NewPrice);

/// <summary>
/// The one way to read or change a product's selling prices (Phase 15A).
///
/// <para><see cref="ProductPrice"/> is the authoritative store; <c>Product.SellingPrice</c> and
/// <c>Product.WholesalePrice</c> are a projection this service keeps in step within the same
/// transaction, so existing POS/report/export readers continue to work untouched. Writing those
/// columns directly bypasses validation, the projection and the audit trail — always come through
/// here.</para>
///
/// <para>15A deliberately stops at storage: nothing here resolves "which level applies to this
/// customer/quantity". That is Phase 15B.</para>
/// </summary>
public interface IProductPricingService
{
    /// <summary>The product's price at one level, or null when that level is not configured.
    /// Reading never writes an audit record (§19).</summary>
    Task<decimal?> GetPriceAsync(int productId, PriceLevel level, CancellationToken cancellationToken = default);

    /// <summary>Every configured active level for a product, retail first.</summary>
    Task<IReadOnlyList<ProductPrice>> GetPricesAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets one level's price, creating the row or updating it in place, and mirrors it onto the
    /// projection column. Requires <see cref="PermissionKeys.ProductsEdit"/> — the permission that
    /// already governs editing a product's catalogue data, including its price.
    ///
    /// <para>Audits only a real change: setting a price to the value it already holds writes
    /// nothing, so the trail stays a record of what actually changed.</para>
    /// </summary>
    Task<ProductPrice> SetPriceAsync(
        int productId, PriceLevel level, decimal price, int? performedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws an optional level, so the product no longer carries that price. Retail cannot be
    /// removed — every product must always have a shelf price.
    /// </summary>
    Task RemovePriceAsync(
        int productId, PriceLevel level, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Throws when the amount is not a storable price. Public so callers can validate
    /// before opening a transaction, and so the rule lives in exactly one place.</summary>
    void ValidatePrice(PriceLevel level, decimal price);

    /// <summary>
    /// Stages a price onto a TRACKED product — updating the <see cref="ProductPrice"/> row and the
    /// projection column together — WITHOUT saving. Returns what changed, or null when the price
    /// already held that value.
    ///
    /// <para>Exists so product creation, product update and product import can each keep their own
    /// single-<c>SaveChangesAsync</c> atomicity while still sharing one implementation of the price
    /// arithmetic and projection. Calling <see cref="SetPriceAsync"/> from inside those flows would
    /// commit pricing separately from the rest of the operation.</para>
    ///
    /// <para><b>Does not check permissions</b>, because it performs no I/O and cannot be reached
    /// except through a caller that has already enforced <see cref="PermissionKeys.ProductsEdit"/>.
    /// Validation IS applied, so no caller can stage a negative price.</para>
    ///
    /// <para>A null <paramref name="price"/> withdraws an optional level; passing null for a
    /// required level (Retail) throws rather than leaving a product with no shelf price.</para>
    /// </summary>
    PriceChange? StagePrice(Product product, PriceLevel level, decimal? price);

    /// <summary>Writes the audit entry for a change returned by <see cref="StagePrice"/>. Callers
    /// invoke this AFTER their own save, so an entry is never left behind by a rolled-back write,
    /// and every caller produces an identically shaped record.</summary>
    Task RecordPriceChangeAsync(
        Product product, PriceChange change, int? performedByUserId, CancellationToken cancellationToken = default);
}
