using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Currencies;

/// <summary>What one currency was worth on one day, resolved and ready to convert with.</summary>
/// <param name="Code">The currency code.</param>
/// <param name="StartingDate">The day the rate came into force.</param>
/// <param name="CurrencyAmount">How many units the pair is quoted for.</param>
/// <param name="BaseAmount">What those units are worth in company currency.</param>
/// <param name="DecimalPlaces">How many places amounts in this currency are rounded to.</param>
public readonly record struct ResolvedRate(
    string Code,
    DateOnly StartingDate,
    decimal CurrencyAmount,
    decimal BaseAmount,
    int DecimalPlaces)
{
    /// <summary>The single multiplier, recorded on every entry the rate converts.</summary>
    public decimal Multiplier => BaseAmount / CurrencyAmount;

    /// <summary>Converts an amount in the foreign currency to the company's own.</summary>
    /// <param name="amount">The amount in the foreign currency.</param>
    /// <returns>The amount in company currency, rounded to two places.</returns>
    public decimal ToBase(decimal amount)
        => Math.Round(amount * BaseAmount / CurrencyAmount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Rounds an amount to the number of places the currency is quoted in.</summary>
    /// <param name="amount">The amount in the foreign currency.</param>
    /// <returns>The rounded amount.</returns>
    public decimal Round(decimal amount)
        => Math.Round(amount, DecimalPlaces, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Answers what a currency was worth on a day.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal here is a refusal to guess. A missing rate is not nought and it is not
/// yesterday's: it is a fact nobody has entered, and inventing one would put a wrong figure into
/// the ledger that reconciles perfectly and is wrong by however much the currency moved. The
/// posting stops and says which currency and which day, which is a five-second fix for whoever
/// keeps the rates and an afternoon's work to find later.
/// </para>
/// <para>
/// Rates are read per posting rather than cached across one, because a document can span dates —
/// a journal batch may hold lines for several days, and each takes the rate of its own day.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class ExchangeRateService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// Finds what a currency was worth on a day.
    /// </summary>
    /// <param name="code">The currency code.</param>
    /// <param name="on">The day, which is the posting date rather than today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The rate, or why there is not one.</returns>
    public async Task<Result<ResolvedRate>> RateOnAsync(
        string code,
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<ResolvedRate>.Failure(
                messages.Render(FinanceMessages.CurrencyNotFound, Args(("Currency", code), ("Date", on))));
        }

        var trimmed = code.Trim().ToUpperInvariant();

        var currency = await context.Set<Currency>()
            .Include(c => c.Rates)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == trimmed, cancellationToken)
            .ConfigureAwait(false);

        if (currency is null)
        {
            return Result<ResolvedRate>.Failure(
                messages.Render(FinanceMessages.CurrencyNotFound, Args(("Currency", trimmed), ("Date", on))));
        }

        if (!currency.IsActive)
        {
            return Result<ResolvedRate>.Failure(
                messages.Render(FinanceMessages.CurrencyBlocked, Args(("Currency", trimmed), ("Date", on))));
        }

        var rate = currency.RateOn(on);

        // A rate that exists and cannot be divided by is worse than one that is missing: it looks
        // answered. Both are reported the same way, because both mean the same thing to whoever
        // has to fix it -- go and enter a usable rate for this day.
        if (rate is null || !rate.IsUsable)
        {
            return Result<ResolvedRate>.Failure(messages.Render(
                FinanceMessages.NoExchangeRate,
                Args(("Currency", trimmed), ("Date", on))));
        }

        return Result<ResolvedRate>.Success(new ResolvedRate(
            currency.Code,
            rate.StartingDate,
            rate.CurrencyAmount,
            rate.BaseAmount,
            currency.DecimalPlaces));
    }

    /// <summary>
    /// Resolves the rate for every currency a set of dated amounts names, in one pass.
    /// </summary>
    /// <param name="wanted">Each currency and the day it is wanted for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// Every rate that resolved, keyed by currency and date, together with every refusal. A
    /// caller that got any refusal has an incomplete dictionary and must not post.
    /// </returns>
    /// <remarks>
    /// Every refusal is collected rather than the first one returned. Somebody who entered three
    /// currencies and has rates for none of them should be told that once, not told about the
    /// first, fix it, and be told about the second.
    /// </remarks>
    public async Task<(Dictionary<(string Code, DateOnly On), ResolvedRate> Rates, List<AsapMessage> Found)>
        ResolveAsync(
            IEnumerable<(string Code, DateOnly On)> wanted,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var resolved = new Dictionary<(string, DateOnly), ResolvedRate>();
        var found = new List<AsapMessage>();

        foreach (var (code, on) in wanted.Distinct())
        {
            var rate = await RateOnAsync(code, on, cancellationToken).ConfigureAwait(false);

            if (rate.Failed)
            {
                found.AddRange(rate.Messages);
                continue;
            }

            resolved[(rate.Value.Code, on)] = rate.Value;
        }

        return (resolved, found);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
