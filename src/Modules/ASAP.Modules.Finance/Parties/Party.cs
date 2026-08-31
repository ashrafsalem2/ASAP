using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Parties;

/// <summary>Which subsidiary ledger a party belongs to.</summary>
public enum PartyKind
{
    /// <summary>Somebody who owes the company money.</summary>
    Customer = 0,

    /// <summary>Somebody the company owes money to.</summary>
    Vendor = 1,
}

/// <summary>
/// Somebody the company trades with on account.
/// </summary>
/// <remarks>
/// <para>
/// Customers and vendors carry the same information and obey the same rules, so they share this
/// base and differ only in which control account they post to and which direction their balance
/// normally runs. Writing the two separately would have meant maintaining the same ageing,
/// application and balance logic twice, and the second copy is the one that quietly drifts.
/// </para>
/// <para>
/// They are still separate tables. A customer and a vendor can share a number without either
/// meaning anything about the other, and the same company being both is ordinary rather than
/// exceptional.
/// </para>
/// </remarks>
public abstract class Party : CompanyEntity
{
    /// <summary>The number the party is known by, for example <c>C-00042</c>.</summary>
    public required string No { get; set; }

    /// <summary>The party's name.</summary>
    public required string Name { get; set; }

    /// <summary>The name in Arabic, as it appears on an Arabic statement or invoice.</summary>
    public string? NameArabic { get; set; }

    /// <summary>Which ledger the party belongs to.</summary>
    public abstract PartyKind Kind { get; }

    /// <summary>
    /// How many days after the document date payment falls due. Drives the due date on every
    /// entry, and through it the whole aged analysis.
    /// </summary>
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>
    /// The most the party may owe before ASAP objects, or zero for no limit.
    /// </summary>
    /// <remarks>
    /// Meaningful on a customer, where it is a real control. Left on the base because a vendor
    /// prepayment limit is the same idea from the other side, and a module that wants one should
    /// not have to add a column to do it.
    /// </remarks>
    public decimal CreditLimit { get; set; }

    /// <summary>
    /// The control account this party posts to, or null to use the company default.
    /// </summary>
    /// <remarks>
    /// An override rather than a requirement. Most installations post every customer to one
    /// receivables account; the ones that do not usually need to split intercompany or retention
    /// balances out on the face of the balance sheet, and that is worth supporting without making
    /// everybody else configure it.
    /// </remarks>
    public string? ControlAccountNo { get; set; }

    /// <summary>Whether the party is withdrawn from use.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// What the party owes, maintained as entries post.
    /// </summary>
    /// <remarks>
    /// Denormalised for the same reason as the account balance: summing a subsidiary ledger to
    /// draw a list of two thousand customers is the difference between a screen that opens and one
    /// that times out. Written only by the posting engine, inside the posting transaction.
    /// </remarks>
    public decimal Balance { get; set; }

    /// <summary>Contact email, used when a statement is sent.</summary>
    public string? Email { get; set; }

    /// <summary>Contact telephone.</summary>
    public string? Phone { get; set; }

    /// <summary>The tax registration number, required on an invoice in most jurisdictions.</summary>
    public string? TaxRegistrationNo { get; set; }

    /// <summary>
    /// Which customer group they are in, on a customer.
    /// </summary>
    /// <remarks>
    /// A fact about who the customer is rather than about any arrangement a module has with them,
    /// which is why it lives here and the price list does not. Promotions asks whether an offer is
    /// for wholesale and Sales asks which list a group is on; neither owns the answer, and a copy
    /// in either would be the one that went stale.
    /// </remarks>
    public string? CustomerGroupCode { get; set; }

    /// <summary>Whether the party may be posted to at all.</summary>
    public bool IsPostable => !IsBlocked && !IsDeleted;
}

/// <summary>Somebody who buys from the company on account.</summary>
public sealed class Customer : Party
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Customer;
}

/// <summary>Somebody the company buys from on account.</summary>
public sealed class Vendor : Party
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Vendor;
}
