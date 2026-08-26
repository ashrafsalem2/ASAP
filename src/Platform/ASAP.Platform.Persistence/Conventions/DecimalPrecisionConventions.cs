using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence.Conventions;

/// <summary>
/// Gives every decimal column an explicit precision.
/// </summary>
/// <remarks>
/// <para>
/// Left alone, SQL Server maps a .NET decimal to <c>decimal(18,2)</c>. For an ERP that is a
/// silent data-loss bug waiting to happen: a unit cost of 0.0125 becomes 0.01, the error is
/// multiplied by the quantity on every movement, and inventory valuation drifts away from the
/// general ledger by an amount nobody can trace. It surfaces months later as an unexplained
/// variance at year end.
/// </para>
/// <para>
/// So the default here is <c>decimal(18,4)</c>, and anything needing more says so explicitly.
/// The named helpers below exist to make those declarations read as what they are.
/// </para>
/// </remarks>
public static class DecimalPrecisionConventions
{
    /// <summary>Money: totals, balances, ledger amounts. Four places covers every currency ASAP trades in.</summary>
    public const string Money = "decimal(19,4)";

    /// <summary>
    /// Quantities. Five places, because goods are sold in fractions of a unit -- metres of cable,
    /// kilogrammes of produce -- and the rounding error compounds through every stock movement.
    /// </summary>
    public const string Quantity = "decimal(18,5)";

    /// <summary>
    /// Unit costs and prices. Five places rather than four: a price per thousand units divides
    /// down to fractions of a fils, and that fraction is what the costing engine works in.
    /// </summary>
    public const string UnitAmount = "decimal(18,5)";

    /// <summary>Percentages, such as a discount or a tax rate.</summary>
    public const string Percentage = "decimal(9,5)";

    /// <summary>
    /// Exchange rates. Six places, because a rate against a weak currency needs them and a
    /// rounded rate applied to a large balance produces a visible error.
    /// </summary>
    public const string ExchangeRate = "decimal(18,6)";

    /// <summary>
    /// Applies the default precision to every decimal property that has not been given one.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    public static void ApplyDecimalPrecision(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(static e => e.GetProperties())
                     .Where(static p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            // Respect anything a configuration already stated. This convention exists to catch
            // what was not thought about, not to overrule what was.
            if (property.GetColumnType() is not null || property.GetPrecision() is not null)
            {
                continue;
            }

            property.SetPrecision(18);
            property.SetScale(4);
        }
    }
}
