using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>
/// Decides which price level a bill should sit at when the customer on it changes (Phase 15B-4).
///
/// <para>Lives here rather than in the POS ViewModel because it is a business rule about money, not
/// UI glue — and because a rule kept in a WinUI assembly cannot be tested by this solution's test
/// project. Pure and side-effect free: it answers a question, it does not apply anything.</para>
///
/// <para>This is a <b>default</b>, never an authority. What a bill actually charges is decided by
/// its own level travelling on <c>CompleteSaleRequest.PriceLevel</c> and resolved server-side;
/// nothing in sale pricing consults a customer record.</para>
/// </summary>
public static class BillPriceLevelPolicy
{
    /// <summary>
    /// The level a bill opens at for a given customer.
    ///
    /// <para>A null preference means "nobody has classified this customer", which behaves as Retail
    /// — the same as a walk-in. That is deliberately not stored as Retail: the two are different
    /// facts that happen to produce the same opening level.</para>
    /// </summary>
    public static PriceLevel ForNewBill(PriceLevel? customerDefault) =>
        customerDefault ?? PriceLevel.Retail;

    /// <summary>
    /// The level a bill should be at after its customer changes.
    ///
    /// <para>Applies the customer's default only while the bill is still <b>empty</b>. Once lines
    /// exist the cashier has quoted those amounts to someone; silently re-pricing them because the
    /// customer field changed would move a price the customer has already been told. In that case
    /// the bill keeps the level it has and the decision stays with the operator, who can switch it
    /// deliberately.</para>
    /// </summary>
    public static PriceLevel WhenCustomerChanges(
        PriceLevel currentBillLevel, PriceLevel? customerDefault, bool cartIsEmpty) =>
        cartIsEmpty ? ForNewBill(customerDefault) : currentBillLevel;
}
