using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Pos.Stations;

/// <summary>
/// A till: one physical point where goods are sold and money is taken.
/// </summary>
/// <remarks>
/// <para>
/// A station is not a user and not a branch. Two cashiers share one till across a shift change,
/// one branch runs six of them, and the cash in each drawer is counted separately — so the thing
/// a session belongs to has to be the drawer, not the person or the shop.
/// </para>
/// <para>
/// It also names where its stock comes from. A till in the Jeddah shop sells what is on the
/// Jeddah shelves, and a receipt that did not know that would take stock out of head office.
/// </para>
/// </remarks>
public sealed class PosStation : CompanyEntity
{
    /// <summary>The station code, for example <c>JED-T1</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The branch it stands in.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>The stock location it sells from.</summary>
    public required string LocationCode { get; set; }

    /// <summary>
    /// The bin this till sells off, where its location tracks them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated once here rather than asked for on every sale, because a cashier cannot answer it.
    /// They took the goods off the shop floor; which shelf the shop floor is on the warehouse map
    /// is a fact about the building, and one somebody can write down in advance.
    /// </para>
    /// <para>
    /// This is not the general default that bins deliberately refuse. The rule that goods leaving
    /// a bin-tracked location must name a bin exists because guessing which shelf they came off
    /// makes a bin hold stock nobody can find. Nothing is guessed here: somebody stated it, for
    /// this till, and it is the same shelf every time.
    /// </para>
    /// <para>
    /// Null at a location that does not track bins, which is most shop floors. Null at one that
    /// does is a till that cannot sell, and it says so rather than failing with a message telling
    /// the cashier to name a bin they have no way to name.
    /// </para>
    /// </remarks>
    public string? PickBinCode { get; set; }

    /// <summary>
    /// The customer a walk-in sale is recorded against.
    /// </summary>
    /// <remarks>
    /// Somebody who pays cash and leaves is still a party to the sale, and the tax return wants a
    /// counterparty on every entry. A single cash-sales customer per station is how that is
    /// answered without asking a queue of people for their names.
    /// </remarks>
    public required string DefaultCustomerNo { get; set; }

    /// <summary>Whether the till may be opened. A retired or faulty one is blocked, not deleted.</summary>
    public bool IsBlocked { get; set; }

}
