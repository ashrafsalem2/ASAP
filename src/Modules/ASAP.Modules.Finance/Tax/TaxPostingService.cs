using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;

namespace ASAP.Modules.Finance.Tax;

/// <summary>One tax figure a posting produced, ready to be written and to be entered in the ledger.</summary>
/// <param name="Line">The line it came from.</param>
/// <param name="Tax">The tax view that produced it.</param>
/// <param name="BaseAmount">The taxable amount.</param>
/// <param name="TaxAmount">The tax on it, signed the same way as the line.</param>
/// <param name="AccountNo">The account the tax lands on.</param>
public readonly record struct ComputedTax(
    PostingLineView Line,
    PostingTaxView Tax,
    decimal BaseAmount,
    decimal TaxAmount,
    string AccountNo);

/// <summary>
/// Works out the tax on a posting, and records it so a return can be built from the ledger.
/// </summary>
/// <remarks>
/// <para>
/// A taxed line becomes two: the line itself and the tax beside it. Which of the two carries the
/// original amount depends on how the figure was quoted. A wholesaler enters 100 net and the tax
/// is added, taking the document to 115. A shop enters 115 off the shelf label and the tax comes
/// out of it, leaving 100 on the revenue line. Both are ordinary and both appear in one company,
/// so the line says which it is rather than the company deciding once for everybody.
/// </para>
/// <para>
/// Nothing here saves. The caller owns the transaction, which is what keeps the tax entry, the
/// general ledger entry and the document itself inseparable.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company and branch being posted in.</param>
public sealed class TaxPostingService(AsapDbContext context, ITenantContext tenantContext)
{
    /// <summary>
    /// Works out the tax on every taxed line.
    /// </summary>
    /// <param name="lines">The lines being posted.</param>
    /// <param name="decimals">Places to round to.</param>
    /// <returns>One entry per taxed line, in the order the lines were given.</returns>
    public static IReadOnlyList<ComputedTax> Compute(
        IReadOnlyList<PostingLineView> lines,
        int decimals = 2)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var computed = new List<ComputedTax>();

        foreach (var line in lines)
        {
            if (line.Tax is not { } tax || tax.Account is null)
            {
                continue;
            }

            var amounts = tax.TaxIncludedInAmount
                ? TaxCalculator.FromGross(line.Amount, tax.Percentage, decimals)
                : TaxCalculator.FromNet(line.Amount, tax.Percentage, decimals);

            // A zero-rated or exempt line still produces an entry, carrying its base and no tax.
            // That row is the only record that the supply happened, and the return has a box for
            // it; dropping it because the tax is nil is how zero-rated sales go undeclared.
            computed.Add(new ComputedTax(
                line,
                tax,
                amounts.Base,
                amounts.Tax,
                tax.Account));
        }

        return computed;
    }

    /// <summary>
    /// Adjusts the posting lines for the tax worked out on them.
    /// </summary>
    /// <param name="lines">The original lines.</param>
    /// <param name="computed">What <see cref="Compute"/> produced.</param>
    /// <param name="taxAccounts">The tax accounts, already resolved.</param>
    /// <returns>The lines to post, including a line for each tax figure.</returns>
    /// <remarks>
    /// A tax-included line is reduced to its base, because the tax was always inside the number
    /// somebody typed. A tax-excluded line keeps its amount, because the tax is being added to it.
    /// Either way the batch balances against what the user entered, which is the only thing they
    /// can check.
    /// </remarks>
    public static IReadOnlyList<PostingLineView> Expand(
        IReadOnlyList<PostingLineView> lines,
        IReadOnlyList<ComputedTax> computed,
        IReadOnlyDictionary<string, PostingAccountView> taxAccounts)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(taxAccounts);

        if (computed.Count == 0)
        {
            return lines;
        }

        var byLine = computed.ToDictionary(static c => c.Line.LineNo);
        var expanded = new List<PostingLineView>(lines.Count + computed.Count);
        var nextLineNo = lines.Count == 0 ? 1 : lines.Max(static l => l.LineNo) + 1;

        foreach (var line in lines)
        {
            if (!byLine.TryGetValue(line.LineNo, out var tax))
            {
                expanded.Add(line);
                continue;
            }

            expanded.Add(tax.Tax.TaxIncludedInAmount ? line with { Amount = tax.BaseAmount } : line);

            if (tax.TaxAmount == 0m || !taxAccounts.TryGetValue(tax.AccountNo, out var account))
            {
                continue;
            }

            expanded.Add(new PostingLineView(
                LineNo: nextLineNo++,
                PostingDate: line.PostingDate,
                Amount: tax.TaxAmount,
                Account: account,
                Dimensions: line.Dimensions,
                DocumentNo: line.DocumentNo,
                Description: $"{tax.Tax.Code} {tax.Tax.Percentage:0.##}%"));
        }

        return expanded;
    }

    /// <summary>
    /// Writes the tax entries a posting produced.
    /// </summary>
    /// <param name="computed">What <see cref="Compute"/> produced.</param>
    /// <param name="request">What the entries should say about themselves.</param>
    /// <param name="transactionNo">The number grouping this posting.</param>
    /// <returns>How many tax entries were written.</returns>
    public int Write(
        IReadOnlyList<ComputedTax> computed,
        IReadOnlyList<PostingLineView> lines,
        PostingRequest request,
        long transactionNo)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(request);

        var documentParty = PartyOf(lines);

        // The vendor's own invoice number is keyed on the vendor line, while the tax code sits on
        // the expense line. Both are properties of the document rather than of a line, so both
        // are read from the posting as a whole.
        var documentReference = lines
            .Select(static l => l.ExternalDocumentNo)
            .FirstOrDefault(static r => !string.IsNullOrWhiteSpace(r));

        foreach (var entry in computed)
        {
            var party = entry.Line.Party ?? documentParty;

            // Stored the way the return reads rather than the way the ledger stores it. A sale
            // credits revenue, so its ledger amount is negative, and a return that printed sales
            // of -1,000 would have every consumer negating it -- each of them a place to get the
            // sign wrong. A credit note is still negative here, because it genuinely reduces the
            // figure being declared.
            var sign = entry.Tax.Direction is TaxDirection.Output ? -1m : 1m;

            context.Set<TaxEntry>().Add(new TaxEntry
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.RequireCompanyId(),
                PostingDate = entry.Line.PostingDate,
                TransactionNo = transactionNo,
                Direction = entry.Tax.Direction,
                TaxCodeId = entry.Tax.Id,
                TaxCodeNo = entry.Tax.Code,
                Kind = entry.Tax.Kind,

                // Copied at posting. A rate change must never restate a figure already declared.
                Percentage = entry.Tax.Percentage,
                BaseAmount = entry.BaseAmount * sign,
                TaxAmount = entry.TaxAmount * sign,
                DocumentType = request.DocumentType,
                DocumentNo = request.DocumentNo,
                ExternalDocumentNo = entry.Line.ExternalDocumentNo ?? documentReference,
                PartyNo = party?.No,
                PartyName = party?.Name,
                PartyTaxRegistrationNo = party?.TaxRegistrationNo,
                TaxAccountNo = entry.AccountNo,
                SourceCode = request.SourceCode,
                BranchId = tenantContext.BranchId,
            });
        }

        return computed.Count;
    }

    /// <summary>
    /// The customer or vendor a posting is about, when there is exactly one.
    /// </summary>
    /// <remarks>
    /// A tax entry has to name who a figure was charged to, because the first thing an auditor
    /// asks of a total is which customers made it up. On a journal the party and the tax code are
    /// on different lines, so it is read from the posting as a whole.
    /// </remarks>
    /// <returns>
    /// The single party, or null when a posting names none or names several. Guessing between two
    /// customers would put a figure against the wrong one, which is worse than leaving it blank
    /// and far harder to notice.
    /// </returns>
    private static PostingPartyView? PartyOf(IReadOnlyList<PostingLineView> lines)
    {
        var parties = lines
            .Select(static l => l.Party)
            .Where(static p => p is not null)
            .DistinctBy(static p => p!.Id)
            .Take(2)
            .ToList();

        return parties.Count == 1 ? parties[0] : null;
    }
}
