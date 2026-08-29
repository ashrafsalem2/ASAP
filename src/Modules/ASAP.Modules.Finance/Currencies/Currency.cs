using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Currencies;

/// <summary>
/// A currency the company transacts in, other than its own.
/// </summary>
/// <remarks>
/// <para>
/// The company's own currency is not in this table. It is on the company record, it never needs a
/// rate against itself, and giving it a row here invites somebody to enter one — at which point
/// every base amount in the system depends on whether that row happens to say 1.
/// </para>
/// <para>
/// The rate lives on <see cref="ExchangeRate"/> rather than here, for the same reason a tax
/// percentage lives on <see cref="Tax.TaxRate"/> rather than on the code: rates change constantly,
/// and an invoice raised last March has to keep last March's rate or it will never settle against
/// the payment that cleared it.
/// </para>
/// </remarks>
public sealed class Currency : CompanyEntity
{
    /// <summary>The ISO 4217 code, for example <c>USD</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The symbol shown beside an amount, for example <c>$</c>.</summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// How many decimal places amounts in it are rounded to.
    /// </summary>
    /// <remarks>
    /// Two for most of the world, three for the Gulf dinars, none for the yen. Rounding a Kuwaiti
    /// dinar to two places loses a fils on every line, and rounding a yen to two invents a
    /// fraction of a unit that no payment can ever be made in.
    /// </remarks>
    public int DecimalPlaces { get; set; } = 2;

    /// <summary>Whether it may still be used on a new document.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The rates this currency has had, each starting on a date.</summary>
    public ICollection<ExchangeRate> Rates { get; set; } = [];

    /// <summary>
    /// The rate in force on a date, or null when the currency had no rate then.
    /// </summary>
    /// <param name="on">The document date, which is what decides the rate.</param>
    /// <returns>The rate, or null.</returns>
    /// <remarks>
    /// The latest rate that had started, not the newest one on file. Entering tomorrow's rate
    /// today is ordinary — a treasury desk publishes them in advance — and it must not reach back
    /// and restate everything posted this morning.
    /// </remarks>
    public ExchangeRate? RateOn(DateOnly on)
        => Rates
            .Where(r => r.StartingDate <= on)
            .OrderByDescending(static r => r.StartingDate)
            .FirstOrDefault();
}

/// <summary>
/// One rate, in force from a date until the next one starts.
/// </summary>
/// <remarks>
/// Held as a pair rather than a single multiplier so a currency worth a small fraction of the
/// company's own can be stated exactly: 100 JPY = 2.53 SAR, not 1 JPY = 0.0253 SAR. The second
/// form is the first rounded, and rounding the rate rather than the amount puts the error into
/// every line instead of into the last one.
/// </remarks>
public sealed class ExchangeRate : CompanyEntity
{
    /// <summary>The currency this rate belongs to.</summary>
    public Guid CurrencyId { get; set; }

    /// <summary>Navigation to the currency.</summary>
    public Currency? Currency { get; set; }

    /// <summary>The first day this rate applies to.</summary>
    public DateOnly StartingDate { get; set; }

    /// <summary>How many units of the foreign currency the pair is quoted for, usually one.</summary>
    public decimal CurrencyAmount { get; set; } = 1m;

    /// <summary>What those units are worth in the company's own currency.</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>Whether the pair is usable at all.</summary>
    public bool IsUsable => CurrencyAmount > 0m && BaseAmount > 0m;

    /// <summary>
    /// Converts an amount in the foreign currency to the company's own.
    /// </summary>
    /// <param name="amount">The amount in the foreign currency.</param>
    /// <returns>The amount in company currency, rounded to two places.</returns>
    /// <remarks>
    /// Rounded here and nowhere else. A conversion carried at full precision through a posting
    /// and rounded at the end produces lines that sum to a hundredth away from zero, and a
    /// transaction that does not balance is refused rather than posted — correctly, and
    /// baffling to whoever raised the invoice.
    /// </remarks>
    public decimal ToBase(decimal amount)
        => Math.Round(amount * BaseAmount / CurrencyAmount, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The single multiplier this pair comes to, for showing on an entry.
    /// </summary>
    /// <remarks>
    /// Recorded on every entry it converts, so an amount posted years ago can still be explained
    /// without anybody having to find what the rate table said at the time — or trust that
    /// nobody has edited it since.
    /// </remarks>
    public decimal Multiplier => BaseAmount / CurrencyAmount;
}
