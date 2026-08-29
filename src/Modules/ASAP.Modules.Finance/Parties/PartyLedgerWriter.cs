using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// Writes the customer and vendor side of a posting.
/// </summary>
/// <remarks>
/// <para>
/// Called by the journal posting service inside the same transaction that writes the general
/// ledger entries, which is the whole point: the control account and the subsidiary ledger are
/// written together or not at all, so they cannot come apart. Every other arrangement -- a second
/// service, a background reconciliation, a nightly job -- eventually produces a receivables
/// control that disagrees with the customer ledger, and nobody finds it until year end.
/// </para>
/// <para>
/// Nothing here saves. The caller owns the transaction.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company and branch being posted in.</param>
public sealed class PartyLedgerWriter(AsapDbContext context, ITenantContext tenantContext)
{
    /// <summary>
    /// Writes a subsidiary entry for every line that names a party, and moves their balances.
    /// </summary>
    /// <param name="lines">The lines being posted.</param>
    /// <param name="request">What the entries should say about themselves.</param>
    /// <param name="transactionNo">The number grouping this posting.</param>
    /// <param name="decimals">How many places to round to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many subsidiary entries were written.</returns>
    public async Task<int> WriteAsync(
        IReadOnlyList<PostingLineView> lines,
        PostingRequest request,
        long transactionNo,
        int decimals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(request);

        var drafts = lines
            .Where(static l => l.Party is not null)
            .Select(line => Draft(line, request, transactionNo, decimals))
            .ToList();

        if (drafts.Count == 0)
        {
            return 0;
        }

        // The two ledgers differ only in which tables they land in, so everything up to here is
        // shared and only the last step knows which pair it is writing.
        foreach (var draft in drafts.Where(static d => d.Kind is PartyKind.Customer))
        {
            context.Set<CustomerLedgerEntry>().Add(Apply(new CustomerLedgerEntry
            {
                PartyNo = draft.PartyNo,
                PartyName = draft.PartyName,
                Description = draft.Description,
                ControlAccountNo = draft.ControlAccountNo,
                SourceCode = draft.SourceCode,
            }, draft));
        }

        foreach (var draft in drafts.Where(static d => d.Kind is PartyKind.Vendor))
        {
            context.Set<VendorLedgerEntry>().Add(Apply(new VendorLedgerEntry
            {
                PartyNo = draft.PartyNo,
                PartyName = draft.PartyName,
                Description = draft.Description,
                ControlAccountNo = draft.ControlAccountNo,
                SourceCode = draft.SourceCode,
            }, draft));
        }

        await MoveBalancesAsync<Customer>(drafts, PartyKind.Customer, cancellationToken).ConfigureAwait(false);
        await MoveBalancesAsync<Vendor>(drafts, PartyKind.Vendor, cancellationToken).ConfigureAwait(false);

        return drafts.Count;
    }

    /// <summary>Everything a subsidiary entry needs, worked out once from the line.</summary>
    private readonly record struct EntryDraft(
        PartyKind Kind,
        Guid PartyId,
        string PartyNo,
        string PartyName,
        DateOnly PostingDate,
        DateOnly DueDate,
        long TransactionNo,
        GlDocumentType DocumentType,
        string? DocumentNo,
        string? ExternalDocumentNo,
        string Description,
        decimal Amount,
        string ControlAccountNo,
        string SourceCode,
        string? CurrencyCode,
        decimal? AmountInCurrency);

    private static EntryDraft Draft(
        PostingLineView line,
        PostingRequest request,
        long transactionNo,
        int decimals)
    {
        var party = line.Party!;

        return new EntryDraft(
            party.Kind,
            party.Id,
            party.No,

            // Copied at posting. A statement printed in three years should say who the party was
            // when the entry was raised, not who has since taken over the name.
            party.Name,
            line.PostingDate,
            line.PostingDate.AddDays(party.PaymentTermsDays),
            transactionNo,
            request.DocumentType,
            request.DocumentNo,
            line.ExternalDocumentNo,
            line.Description ?? request.Description ?? party.Name,
            Math.Round(line.Amount, decimals, MidpointRounding.AwayFromZero),
            party.ControlAccountNo,
            request.SourceCode,
            line.CurrencyCode,
            line.AmountInCurrency);
    }

    private TEntry Apply<TEntry>(TEntry entry, EntryDraft draft)
        where TEntry : PartyLedgerEntry
    {
        entry.TenantId = tenantContext.TenantId ?? Guid.Empty;
        entry.CompanyId = tenantContext.RequireCompanyId();
        entry.PartyId = draft.PartyId;
        entry.PostingDate = draft.PostingDate;
        entry.DueDate = draft.DueDate;
        entry.TransactionNo = draft.TransactionNo;
        entry.DocumentType = draft.DocumentType;
        entry.DocumentNo = draft.DocumentNo;
        entry.ExternalDocumentNo = draft.ExternalDocumentNo;
        entry.Amount = draft.Amount;

        // Nothing is settled at the moment of posting. Applications come afterwards, even when a
        // payment is keyed against an invoice in the same breath.
        entry.RemainingAmount = draft.Amount;
        entry.CurrencyCode = draft.CurrencyCode;
        entry.AmountInCurrency = draft.AmountInCurrency;
        entry.RemainingAmountInCurrency = draft.AmountInCurrency;
        entry.IsOpen = true;
        entry.BranchId = tenantContext.BranchId;

        return entry;
    }

    private async Task MoveBalancesAsync<TParty>(
        List<EntryDraft> drafts,
        PartyKind kind,
        CancellationToken cancellationToken)
        where TParty : Party
    {
        var movements = drafts
            .Where(d => d.Kind == kind)
            .GroupBy(static d => d.PartyId)
            .ToDictionary(static g => g.Key, static g => g.Sum(static d => d.Amount));

        if (movements.Count == 0)
        {
            return;
        }

        var ids = movements.Keys.ToList();

        // Tracked, not AsNoTracking: these balances are about to move and must be saved with
        // everything else in this transaction.
        var parties = await context.Set<TParty>()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var party in parties)
        {
            party.Balance += movements[party.Id];
        }
    }
}
