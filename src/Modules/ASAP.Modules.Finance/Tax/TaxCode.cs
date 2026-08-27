using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Tax;

/// <summary>
/// How a tax code behaves, which decides far more than its percentage.
/// </summary>
/// <remarks>
/// Zero-rated and exempt both charge nothing, and are not the same thing. A zero-rated sale is a
/// taxable sale at 0%, belongs on the return, and leaves the input tax on its costs recoverable.
/// An exempt sale is outside the tax entirely, is reported differently, and can restrict what the
/// business may reclaim. Collapsing them into "rate = 0" produces a return that is wrong in a way
/// no arithmetic check would ever catch.
/// </remarks>
public enum TaxKind
{
    /// <summary>Taxed at the code's rate.</summary>
    Standard = 0,

    /// <summary>Taxable, but at nothing. Still reported as a taxable supply.</summary>
    ZeroRated = 1,

    /// <summary>Outside the tax. Reported separately, and may restrict recovery.</summary>
    Exempt = 2,

    /// <summary>
    /// The buyer accounts for both sides. Common on imported services: the same amount is
    /// declared as output tax and reclaimed as input tax, so the net cash effect is nil while
    /// both figures must still appear on the return.
    /// </summary>
    ReverseCharge = 3,
}

/// <summary>Which side of the business a tax figure belongs to.</summary>
public enum TaxDirection
{
    /// <summary>Tax charged to a customer. Owed to the authority.</summary>
    Output = 0,

    /// <summary>Tax paid to a vendor. Reclaimable from the authority.</summary>
    Input = 1,
}

/// <summary>
/// A tax an invoice line can carry, such as standard-rated VAT.
/// </summary>
/// <remarks>
/// <para>
/// The percentage lives on <see cref="TaxRate"/> rather than here, because rates change and old
/// documents must keep the rate they were raised under. Saudi Arabia went from 5% to 15% in July
/// 2020; a system holding one number on the code would have silently restated every historical
/// invoice the moment somebody edited it.
/// </para>
/// </remarks>
public sealed class TaxCode : CompanyEntity
{
    /// <summary>Short stable code, for example <c>VAT15</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is, as it appears on a document and on the return.</summary>
    public required string Description { get; set; }

    /// <summary>The Arabic description, as printed on an Arabic tax invoice.</summary>
    public string? DescriptionArabic { get; set; }

    /// <summary>How the code behaves.</summary>
    public TaxKind Kind { get; set; } = TaxKind.Standard;

    /// <summary>
    /// Where output tax lands: what the company owes the authority on its sales.
    /// </summary>
    public string? OutputAccountNo { get; set; }

    /// <summary>
    /// Where input tax lands: what the company may reclaim on its purchases.
    /// </summary>
    public string? InputAccountNo { get; set; }

    /// <summary>Whether the code may still be used on a new document.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The rates this code has had, each starting on a date.
    /// </summary>
    public ICollection<TaxRate> Rates { get; set; } = [];

    /// <summary>
    /// The percentage in force on a date, or null when the code had no rate then.
    /// </summary>
    /// <param name="on">The document date, which is what decides the rate.</param>
    /// <returns>The rate as a percentage, so 15 means 15%.</returns>
    /// <remarks>
    /// The latest rate that had started, not the newest one on file. A credit note against a
    /// 2019 invoice has to carry 2019's rate, or it will not offset the invoice it corrects.
    /// </remarks>
    public decimal? RateOn(DateOnly on)
    {
        if (Kind is TaxKind.ZeroRated or TaxKind.Exempt)
        {
            return 0m;
        }

        return Rates
            .Where(r => r.StartingDate <= on)
            .OrderByDescending(static r => r.StartingDate)
            .Select(static r => (decimal?)r.Percentage)
            .FirstOrDefault();
    }
}

/// <summary>
/// One percentage, in force from a date until the next one starts.
/// </summary>
public sealed class TaxRate : CompanyEntity
{
    /// <summary>The code this rate belongs to.</summary>
    public Guid TaxCodeId { get; set; }

    /// <summary>Navigation to the code.</summary>
    public TaxCode? TaxCode { get; set; }

    /// <summary>The first day this percentage applies to.</summary>
    public DateOnly StartingDate { get; set; }

    /// <summary>The percentage, so 15 means 15%.</summary>
    public decimal Percentage { get; set; }
}
