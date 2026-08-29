using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Parties;

/// <summary>What an application changed.</summary>
/// <param name="AppliedAmount">How much moved.</param>
/// <param name="FromRemaining">What is left unapplied on the paying entry.</param>
/// <param name="ToRemaining">What is left outstanding on the entry that was settled.</param>
/// <param name="ClosedEntries">How many entries this closed.</param>
public readonly record struct ApplicationReceipt(
    decimal AppliedAmount,
    decimal FromRemaining,
    decimal ToRemaining,
    int ClosedEntries);

/// <summary>
/// Settles one ledger entry against another: this payment paid that invoice.
/// </summary>
/// <remarks>
/// <para>
/// Applying nothing to the general ledger is the point worth understanding. The money has already
/// moved -- the payment was posted, the bank was debited, the control account was credited. What
/// is left is a bookkeeping question about <em>which</em> invoice it settled, and that question
/// only has consequences inside the subsidiary ledger: what shows as outstanding, what appears on
/// a statement, and where a balance falls in the aged analysis.
/// </para>
/// <para>
/// So this writes no journal, and a user with permission to apply need not have permission to
/// post. They are genuinely different powers: one moves money, the other decides what the account
/// is shown as owing.
/// </para>
/// <para>
/// Foreign currency is the one exception, and it is a real one rather than a leak. An invoice for
/// USD 1,000 raised at 3.75 and settled by a payment of USD 1,000 at 3.80 is paid in full -- the
/// customer owes nothing, and a system that left 50 riyals outstanding would put a chaser in
/// front of somebody who has paid. But the control account was debited 3,750 and credited 3,800,
/// so 50 riyals sit on it belonging to nobody. That 50 is not an unpaid balance; it is what the
/// same thousand dollars was worth on two different days, and it is posted as exactly that. The
/// alternative is a receivables account that does not agree with the ledger behind it, which is
/// the single thing this module exists to prevent.
/// </para>
/// <para>
/// Every application is a row rather than an adjustment to a number, which is what makes unapply
/// possible. Giving each side back exactly what one row took is arithmetic; unpicking it from the
/// remaining balances alone would be guesswork.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenantContext">Supplies the company.</param>
/// <param name="userContext">Records who applied.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="exchange">
/// Posts an exchange difference, on the rare occasion there is one. A one-method seam rather than
/// the whole posting stack: applying a payment to an invoice normally writes no journal at all.
/// </param>
public sealed class PartyApplicationService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    IExchangeDifferencePoster exchange)
{
    /// <summary>
    /// Applies one entry against another.
    /// </summary>
    /// <param name="kind">Which ledger the entries belong to.</param>
    /// <param name="fromEntryId">The entry the money comes from, normally a payment.</param>
    /// <param name="toEntryId">The entry being settled, normally an invoice.</param>
    /// <param name="amount">
    /// How much to apply, always positive, or null to apply as much as both sides allow.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What changed, or every reason the application was refused.</returns>
    public Task<Result<ApplicationReceipt>> ApplyAsync(
        PartyKind kind,
        Guid fromEntryId,
        Guid toEntryId,
        decimal? amount = null,
        CancellationToken cancellationToken = default)
        => kind is PartyKind.Customer
            ? ApplyAsync<CustomerLedgerEntry, CustomerApplication>(
                fromEntryId, toEntryId, amount, kind, cancellationToken)
            : ApplyAsync<VendorLedgerEntry, VendorApplication>(
                fromEntryId, toEntryId, amount, kind, cancellationToken);

    /// <summary>
    /// Undoes an application, giving both entries back what it took.
    /// </summary>
    /// <param name="kind">Which ledger the application belongs to.</param>
    /// <param name="applicationId">The application to undo.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What changed, or the reason it could not be undone.</returns>
    public Task<Result<ApplicationReceipt>> UnapplyAsync(
        PartyKind kind,
        Guid applicationId,
        CancellationToken cancellationToken = default)
        => kind is PartyKind.Customer
            ? UnapplyAsync<CustomerLedgerEntry, CustomerApplication>(applicationId, kind, cancellationToken)
            : UnapplyAsync<VendorLedgerEntry, VendorApplication>(applicationId, kind, cancellationToken);

    private async Task<Result<ApplicationReceipt>> ApplyAsync<TEntry, TApplication>(
        Guid fromEntryId,
        Guid toEntryId,
        decimal? amount,
        PartyKind kind,
        CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
        where TApplication : PartyApplication, new()
    {
        var from = await FindAsync<TEntry>(fromEntryId, cancellationToken).ConfigureAwait(false);
        var to = await FindAsync<TEntry>(toEntryId, cancellationToken).ConfigureAwait(false);

        if (from is null || to is null)
        {
            return Refuse(FinanceMessages.ApplicationEntryNotFound, new()
            {
                ["PartyKind"] = kind.ToString().ToLowerInvariant(),
            });
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FromDocumentNo"] = Label(from),
            ["ToDocumentNo"] = Label(to),
            ["FromPartyNo"] = from.PartyNo,
            ["ToPartyNo"] = to.PartyNo,
        };

        if (from.PartyId != to.PartyId)
        {
            return Refuse(FinanceMessages.ApplicationDifferentParties, arguments);
        }

        if (!from.IsOpen || !to.IsOpen)
        {
            arguments["DocumentNo"] = Label(from.IsOpen ? to : from);
            return Refuse(FinanceMessages.ApplicationEntryClosed, arguments);
        }

        // Both sides pulling the same way is the mistake worth naming: applying an invoice to an
        // invoice would raise what is outstanding rather than settle it.
        if (Math.Sign(from.RemainingAmount) == Math.Sign(to.RemainingAmount))
        {
            return Refuse(FinanceMessages.ApplicationSameDirection, arguments);
        }

        // One currency or the other, never a mixture. Comparing a dollar entry with a riyal one
        // would mean choosing a rate to compare them at, and whichever was chosen would decide a
        // gain or a loss that nobody asked for.
        if (!string.Equals(from.CurrencyCode, to.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            arguments["FromCurrency"] = from.CurrencyCode ?? BaseLabel;
            arguments["ToCurrency"] = to.CurrencyCode ?? BaseLabel;

            return Refuse(FinanceMessages.ApplicationDifferentCurrencies, arguments);
        }

        // For a foreign pair the question "how much is settled" is asked and answered in the
        // currency the two were written in. That is what the customer paid and what the invoice
        // said; the riyal figures are two valuations of it, and neither is the debt.
        var foreign = from.CurrencyCode is { Length: > 0 }
                      && from.RemainingAmountInCurrency is not null
                      && to.RemainingAmountInCurrency is not null;

        var available = Math.Abs(foreign ? from.RemainingAmountInCurrency!.Value : from.RemainingAmount);
        var outstanding = Math.Abs(foreign ? to.RemainingAmountInCurrency!.Value : to.RemainingAmount);
        var applied = amount ?? Math.Min(available, outstanding);

        if (applied <= 0m || applied > available || applied > outstanding)
        {
            arguments["Amount"] = applied;
            arguments["Available"] = available;
            arguments["Outstanding"] = outstanding;

            return Refuse(FinanceMessages.ApplicationTooLarge, arguments);
        }

        var fromBase = BaseShare(from, applied, foreign);
        var toBase = BaseShare(to, applied, foreign);

        // What each side takes off the control account, signed. They pull opposite ways, so for
        // anything in one currency they cancel exactly and this is nought.
        var difference = (Math.Sign(from.RemainingAmount) * fromBase)
                         + (Math.Sign(to.RemainingAmount) * toBase);

        long? exchangeTransactionNo = null;
        var found = new List<AsapMessage>();

        if (difference != 0m)
        {
            var posted = await exchange
                .PostAsync(
                    to.ControlAccountNo,
                    difference,
                    from.CurrencyCode,
                    to.DocumentNo,
                    to.BranchId ?? from.BranchId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (posted.Failed)
            {
                return Result<ApplicationReceipt>.FailureFrom(posted);
            }

            exchangeTransactionNo = posted.Value;
            found.AddRange(posted.Messages);
        }

        // Each side moves towards zero: by its own base share, and by the shared currency amount.
        Settle(from, -Math.Sign(from.RemainingAmount) * fromBase, foreign ? applied : null);
        Settle(to, -Math.Sign(to.RemainingAmount) * toBase, foreign ? applied : null);

        context.Set<TApplication>().Add(new TApplication
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            AppliedFromEntryId = from.Id,
            AppliedToEntryId = to.Id,
            PartyId = from.PartyId,
            AppliedOn = clock.Today,
            Amount = applied,
            AmountInCurrency = foreign ? applied : null,
            FromBaseAmount = fromBase,
            ToBaseAmount = toBase,
            ExchangeDifference = difference,
            ExchangeTransactionNo = exchangeTransactionNo,
            AppliedBy = userContext.UserId,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var closed = (from.IsOpen ? 0 : 1) + (to.IsOpen ? 0 : 1);

        return Result<ApplicationReceipt>.Success(
            new ApplicationReceipt(applied, from.RemainingAmount, to.RemainingAmount, closed),
            found);
    }

    /// <summary>
    /// How much of an entry's company-currency amount one application takes off it.
    /// </summary>
    /// <remarks>
    /// Settling an entry in full takes the whole of what is left, rather than a share computed
    /// from the rate and rounded. The two differ by a hundredth often enough to matter, and the
    /// version that rounds leaves entries a penny open forever -- which is the failure this
    /// whole arrangement exists to avoid, arrived at from the other side.
    /// </remarks>
    private static decimal BaseShare(PartyLedgerEntry entry, decimal applied, bool foreign)
    {
        if (!foreign)
        {
            return applied;
        }

        var remainingInCurrency = Math.Abs(entry.RemainingAmountInCurrency ?? 0m);

        if (remainingInCurrency == 0m)
        {
            return 0m;
        }

        return applied >= remainingInCurrency
            ? Math.Abs(entry.RemainingAmount)
            : Math.Round(
                Math.Abs(entry.RemainingAmount) * applied / remainingInCurrency,
                2,
                MidpointRounding.AwayFromZero);
    }

    private async Task<Result<ApplicationReceipt>> UnapplyAsync<TEntry, TApplication>(
        Guid applicationId,
        PartyKind kind,
        CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
        where TApplication : PartyApplication
    {
        var application = await context.Set<TApplication>()
            .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsReversed, cancellationToken)
            .ConfigureAwait(false);

        if (application is null)
        {
            return Refuse(FinanceMessages.ApplicationEntryNotFound, new()
            {
                ["PartyKind"] = kind.ToString().ToLowerInvariant(),
            });
        }

        var from = await FindAsync<TEntry>(application.AppliedFromEntryId, cancellationToken).ConfigureAwait(false);
        var to = await FindAsync<TEntry>(application.AppliedToEntryId, cancellationToken).ConfigureAwait(false);

        if (from is null || to is null)
        {
            return Refuse(FinanceMessages.ApplicationEntryNotFound, new()
            {
                ["PartyKind"] = kind.ToString().ToLowerInvariant(),
            });
        }

        // Exactly what was taken, given back. Recomputing from the amounts would be a guess as
        // soon as a second application touched either side -- and for a foreign pair the two
        // sides did not take the same figure, which is why both are recorded.
        Settle(
            from,
            application.FromBaseAmount * Math.Sign(from.Amount),
            application.AmountInCurrency);
        Settle(
            to,
            application.ToBaseAmount * Math.Sign(to.Amount),
            application.AmountInCurrency);

        // The row stays, marked. A statement should still be able to say that an application was
        // made and then withdrawn, and on which date.
        application.IsReversed = true;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ApplicationReceipt>.Success(
            new ApplicationReceipt(application.Amount, from.RemainingAmount, to.RemainingAmount, 0));
    }

    /// <summary>
    /// Moves an entry's remaining amount and closes it when nothing is left.
    /// </summary>
    /// <remarks>
    /// A foreign entry is open or closed according to what is left in its own currency, not in
    /// the company's. The two answer differently the moment a rate moves, and the currency one is
    /// the one that says whether anybody still owes anything.
    /// </remarks>
    private void Settle(PartyLedgerEntry entry, decimal movement, decimal? movementInCurrency)
    {
        entry.RemainingAmount += movement;

        if (movementInCurrency is { } inCurrency && entry.RemainingAmountInCurrency is { } remaining)
        {
            entry.RemainingAmountInCurrency = remaining + (Math.Sign(movement) * inCurrency);
            entry.IsOpen = entry.RemainingAmountInCurrency != 0m;
        }
        else
        {
            entry.IsOpen = entry.RemainingAmount != 0m;
        }

        entry.ClosedOn = entry.IsOpen ? null : clock.Today;
    }

    /// <summary>How the company's own currency is named in a message about two currencies.</summary>
    private const string BaseLabel = "the company's own";

    private Task<TEntry?> FindAsync<TEntry>(Guid id, CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
        => context.Set<TEntry>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <summary>How an entry is named in a message: its document number, or the transaction.</summary>
    private static string Label(PartyLedgerEntry entry)
        => entry.DocumentNo ?? $"#{entry.TransactionNo}";

    private Result<ApplicationReceipt> Refuse(MessageCode code, Dictionary<string, object?> arguments)
        => Result<ApplicationReceipt>.Failure(messages.Render(code, arguments));
}
