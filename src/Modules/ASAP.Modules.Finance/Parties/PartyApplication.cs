using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// A record that one entry settled part of another: this payment paid that invoice.
/// </summary>
/// <remarks>
/// <para>
/// Kept as its own row rather than inferred from the remaining amounts, because the remaining
/// amount says <em>how much</em> is left but never <em>who</em> settled it. The question a credit
/// controller actually asks is which payment cleared which invoice, and on an account where five
/// payments cover seven invoices no amount of arithmetic recovers that after the fact.
/// </para>
/// <para>
/// It is also what makes unapplying possible. Reversing a mistaken match means giving each side
/// back exactly what this row took from it, and without the row the only honest answer would be
/// to reopen everything and ask somebody to sort it out by hand.
/// </para>
/// </remarks>
public abstract class PartyApplication : LedgerEntity
{
    /// <summary>The entry the money came from, normally a payment or credit memo.</summary>
    public Guid AppliedFromEntryId { get; set; }

    /// <summary>The entry being settled, normally an invoice.</summary>
    public Guid AppliedToEntryId { get; set; }

    /// <summary>The party both entries belong to.</summary>
    public Guid PartyId { get; set; }

    /// <summary>The date the application was made.</summary>
    public DateOnly AppliedOn { get; set; }

    /// <summary>
    /// How much moved, always positive. Which direction it moved is decided by the two entries,
    /// not by the sign here.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Who made the application.</summary>
    public Guid? AppliedBy { get; set; }

    /// <summary>
    /// True once the application has been undone. The row stays, so a statement can still explain
    /// what happened and when it was reversed.
    /// </summary>
    public bool IsReversed { get; set; }

    /// <summary>Which ledger this belongs to.</summary>
    public abstract PartyKind Kind { get; }
}

/// <summary>One customer payment settling one customer invoice.</summary>
public sealed class CustomerApplication : PartyApplication
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Customer;
}

/// <summary>One vendor payment settling one vendor invoice.</summary>
public sealed class VendorApplication : PartyApplication
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Vendor;
}
