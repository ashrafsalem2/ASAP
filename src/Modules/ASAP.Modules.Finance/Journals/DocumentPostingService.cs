using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Posting;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Journals;

/// <summary>What a caller wants posted, in the terms a document is written in.</summary>
/// <param name="BatchCode">The batch or document the lines belong to, named in messages.</param>
/// <param name="Lines">The lines to post.</param>
/// <param name="SourceCode">
/// Where the entries came from, such as <c>SALES</c> or <c>GENJNL</c>. Carried onto every entry,
/// and it is what somebody filters the ledger by when they want to see only the invoices.
/// </param>
/// <param name="IsManualEntry">
/// Whether a person keyed these lines. Deliberately has no default: a module that forgot to say
/// would otherwise silently acquire, or silently lose, the protection that keeps hand-keyed
/// entries out of control accounts.
/// </param>
/// <param name="DocumentType">What kind of document this is, for reporting.</param>
/// <param name="DocumentNo">The document number the entries carry.</param>
/// <param name="Description">Default description for lines that supply none.</param>
/// <param name="OverrideReason">
/// Why the caller is pushing past a block. Recorded in the audit log alongside the code overridden.
/// </param>
public sealed record DocumentPosting(
    string BatchCode,
    IReadOnlyList<PostJournalLine> Lines,
    string SourceCode,
    bool IsManualEntry,
    GlDocumentType DocumentType = GlDocumentType.None,
    string? DocumentNo = null,
    string? Description = null,
    string? OverrideReason = null);

/// <summary>
/// Turns what a document names into what the posting engine needs, then posts it.
/// </summary>
/// <remarks>
/// The whole job here is translation: account numbers into accounts, party numbers into parties,
/// tax codes into the rate that was in force on the day. Every actual rule lives in the validator,
/// where it can be tested without any of this.
/// <para>
/// It is a service rather than part of the journal command because a sales invoice needs exactly
/// the same translation and is emphatically not a manual journal. Posting one through the command
/// made the module's own revenue account look hand-keyed, so invoicing worked only for somebody
/// holding <c>Finance.Account.Override</c> -- which is to say, not for the salesperson whose job
/// it is.
/// </para>
/// </remarks>
public sealed class DocumentPostingService(
    AsapDbContext context,
    JournalPostingService posting,
    ISetupService setup,
    IMessageCatalog messages,
    IUserContext userContext,
    IClock clock)
{
    /// <summary>Resolves and posts a document.</summary>
    /// <param name="request">What to post.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The receipt, or the messages explaining why nothing was posted.</returns>
    public async Task<Result<PostingReceipt>> PostAsync(
        DocumentPosting request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = clock.Today;

        var parties = await ResolvePartiesAsync(request.Lines, cancellationToken).ConfigureAwait(false);
        var taxCodes = await ResolveTaxCodesAsync(request.Lines, cancellationToken).ConfigureAwait(false);

        // A party line posts to its control account, so that account has to be loaded alongside
        // the ones the document names directly.
        var accountNumbers = request.Lines
            .SelectMany(l => new[]
            {
                l.AccountType is JournalAccountType.GlAccount ? l.AccountNo : ControlAccountFor(l, parties),
                l.BalancingAccountNo,

                // A tax line is generated during posting rather than named by the user, so its
                // account has to be loaded here with everything else. Both sides, because which
                // one applies depends on the party, resolved separately below.
                TaxAccountFor(l, taxCodes, TaxDirection.Output),
                TaxAccountFor(l, taxCodes, TaxDirection.Input),
            })
            .Where(static no => !string.IsNullOrWhiteSpace(no))
            .Select(static no => no!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Loaded in one query rather than per line. A payroll journal names the same handful of
        // accounts across two hundred lines, and a lookup per line would be two hundred queries
        // to answer four questions.
        var accounts = await context.Set<GlAccount>()
            .AsNoTracking()
            .Where(a => accountNumbers.Contains(a.No))
            .ToDictionaryAsync(static a => a.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var companyDimensionIds = await context.Dimensions
            .AsNoTracking()
            .Where(d => !d.IsBlocked)
            .Select(static d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mandatory = await context.Dimensions
            .AsNoTracking()
            .Where(d => d.IsMandatory && !d.IsBlocked)
            .Select(static d => new { d.Id, d.Code, d.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var calendar = await FiscalCalendar.LoadAsync(context, cancellationToken).ConfigureAwait(false);

        // Which side of the tax return a figure belongs to is a property of the document, not
        // of the line. A sales invoice that shows its discount separately has a revenue line
        // and a contra line; judged by sign alone the contra reads as a purchase, and its tax
        // would be claimed back instead of reducing what is owed.
        var documentKind = DocumentPartyKind(request.Lines, parties);

        var lines = request.Lines
            .Select((line, index) =>
            {
                var party = PartyFor(line, parties);

                return new PostingLineView(
                    LineNo: index + 1,
                    PostingDate: line.PostingDate ?? today,
                    Amount: line.Amount,
                    Account: Resolve(
                        line.AccountType is JournalAccountType.GlAccount
                            ? line.AccountNo
                            : party?.ControlAccountNo,
                        accounts,
                        companyDimensionIds),
                    BalancingAccount: Resolve(line.BalancingAccountNo, accounts, companyDimensionIds),
                    Dimensions: DimensionCombination.Empty,
                    DocumentNo: request.DocumentNo,
                    Description: line.Description,
                    Party: party,
                    ExternalDocumentNo: line.ExternalDocumentNo,
                    Tax: TaxFor(line, party?.Kind ?? documentKind, taxCodes, line.PostingDate ?? today));
            })
            .ToList();

        // A party number matching nothing is reported here rather than inside the validator, which
        // never sees the number the caller gave -- only the party it failed to resolve to.
        var unknown = UnknownParties(request.Lines, parties);

        if (unknown.Count > 0)
        {
            return Result<PostingReceipt>.Failure(unknown);
        }

        var environment = new PostingEnvironment(
            BatchCode: request.BatchCode,
            CurrencyCode: await BaseCurrencyAsync(cancellationToken).ConfigureAwait(false),
            ResolvePeriod: calendar.Resolve,
            CurrencyDecimals: 2,
            PostingWindowFrom: await setup
                .GetAtScopeAsync<DateOnly?>($"{FinanceModule.Id}.Posting.AllowFrom", SetupScope.Company, null, cancellationToken)
                .ConfigureAwait(false),
            PostingWindowTo: await setup
                .GetAtScopeAsync<DateOnly?>($"{FinanceModule.Id}.Posting.AllowTo", SetupScope.Company, null, cancellationToken)
                .ConfigureAwait(false),
            MandatoryDimensions:
            [
                .. mandatory.Select(d => new MandatoryDimensionView(d.Id, d.Code, d.Name)),
            ],

            // Only the overrides this caller actually holds. The validator downgrades a block to a
            // warning when the permission is present, and the posting service audits the fact.
            HeldOverridePermissions: HeldOverrides(),
            IsManualEntry: request.IsManualEntry,

            // Every account a generated tax line might land on. The tax accounts are control
            // accounts, so this is also what lets a tax line reach one without an override.
            TaxAccountsByNo: TaxAccounts(taxCodes, accounts, companyDimensionIds));

        var postingRequest = new PostingRequest(
            SourceCode: request.SourceCode,
            DocumentType: request.DocumentType,
            DocumentNo: request.DocumentNo,
            Description: request.Description,
            OverrideReason: request.OverrideReason);

        return await posting
            .PostAsync(lines, environment, postingRequest, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every customer and vendor the document names, keyed by kind and number.
    /// </summary>
    /// <remarks>
    /// Two queries at most, whatever the batch holds. A purchase day book names forty vendors
    /// across two hundred lines, and resolving per line would be two hundred round trips.
    /// </remarks>
    private async Task<Dictionary<(JournalAccountType Type, string No), PostingPartyView>> ResolvePartiesAsync(
        IReadOnlyList<PostJournalLine> lines,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<(JournalAccountType, string), PostingPartyView>();

        var customerNos = NumbersOf(lines, JournalAccountType.Customer);
        var vendorNos = NumbersOf(lines, JournalAccountType.Vendor);

        if (customerNos.Count == 0 && vendorNos.Count == 0)
        {
            return resolved;
        }

        var receivables = await ControlAccountAsync(
                $"{FinanceModule.Id}.Parties.ReceivablesAccount",
                cancellationToken)
            .ConfigureAwait(false);

        var payables = await ControlAccountAsync(
                $"{FinanceModule.Id}.Parties.PayablesAccount",
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var customer in await LoadAsync<Customer>(customerNos, cancellationToken).ConfigureAwait(false))
        {
            resolved[(JournalAccountType.Customer, customer.No)] =
                PostingPartyView.From(customer, receivables);
        }

        foreach (var vendor in await LoadAsync<Vendor>(vendorNos, cancellationToken).ConfigureAwait(false))
        {
            resolved[(JournalAccountType.Vendor, vendor.No)] = PostingPartyView.From(vendor, payables);
        }

        return resolved;
    }

    private static List<string> NumbersOf(IReadOnlyList<PostJournalLine> lines, JournalAccountType type)
        => [.. lines
            .Where(l => l.AccountType == type)
            .Select(static l => l.AccountNo)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private Task<List<TParty>> LoadAsync<TParty>(List<string> numbers, CancellationToken cancellationToken)
        where TParty : Party
        => numbers.Count == 0
            ? Task.FromResult(new List<TParty>())
            : context.Set<TParty>()
                .AsNoTracking()
                .Where(p => numbers.Contains(p.No))
                .ToListAsync(cancellationToken);

    private async Task<string> ControlAccountAsync(string setupKey, CancellationToken cancellationToken)
        => await setup.GetAsync<string>(setupKey, cancellationToken).ConfigureAwait(false)
           ?? string.Empty;

    private static PostingPartyView? PartyFor(
        PostJournalLine line,
        IReadOnlyDictionary<(JournalAccountType Type, string No), PostingPartyView> parties)
        => line.AccountType is JournalAccountType.GlAccount
            ? null
            : parties.GetValueOrDefault((line.AccountType, line.AccountNo));

    private static string? ControlAccountFor(
        PostJournalLine line,
        IReadOnlyDictionary<(JournalAccountType Type, string No), PostingPartyView> parties)
        => PartyFor(line, parties)?.ControlAccountNo;

    /// <summary>Builds a refusal for every line naming a party that does not exist.</summary>
    private List<AsapMessage> UnknownParties(
        IReadOnlyList<PostJournalLine> lines,
        IReadOnlyDictionary<(JournalAccountType Type, string No), PostingPartyView> parties)
    {
        var found = new List<AsapMessage>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (line.AccountType is JournalAccountType.GlAccount
                || parties.ContainsKey((line.AccountType, line.AccountNo)))
            {
                continue;
            }

            found.Add(messages.Render(
                FinanceMessages.PartyNotFound,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LineNo"] = index + 1,
                    ["PartyNo"] = line.AccountNo,
                    ["PartyKind"] = line.AccountType.ToString().ToLowerInvariant(),
                },
                MessageTarget.OnField($"Lines[{index + 1}]")));
        }

        return found;
    }

    /// <summary>Loads every tax code the document names, with its rates.</summary>
    private async Task<Dictionary<string, TaxCode>> ResolveTaxCodesAsync(
        IReadOnlyList<PostJournalLine> lines,
        CancellationToken cancellationToken)
    {
        var codes = lines
            .Select(static l => l.TaxCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, TaxCode>(StringComparer.OrdinalIgnoreCase);
        }

        return await context.Set<TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => codes.Contains(c.Code) && c.IsActive)
            .ToDictionaryAsync(static c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the tax view for a line, resolving the rate that was in force on its own date.
    /// </summary>
    /// <remarks>
    /// Which side of the return a figure belongs to follows the party where there is one, and the
    /// sign of the amount where there is not. A journal line debiting an expense is money going
    /// out, so its tax is input tax; a credit to revenue is money coming in, so its tax is output.
    /// </remarks>
    private static PostingTaxView? TaxFor(
        PostJournalLine line,
        PartyKind? partyKind,
        IReadOnlyDictionary<string, TaxCode> taxCodes,
        DateOnly postingDate)
    {
        if (string.IsNullOrWhiteSpace(line.TaxCode)
            || !taxCodes.TryGetValue(line.TaxCode, out var code))
        {
            return null;
        }

        var direction = partyKind switch
        {
            PartyKind.Customer => TaxDirection.Output,
            PartyKind.Vendor => TaxDirection.Input,

            // No party anywhere on the document, so there is nothing to follow but the sign.
            // A credit is money coming in and a debit is money going out.
            _ => line.Amount < 0m ? TaxDirection.Output : TaxDirection.Input,
        };

        return new PostingTaxView(
            code.Id,
            code.Code,
            code.Kind,
            direction,

            // The rate on the line's date, not today's. A credit note against an old invoice has
            // to carry the rate that invoice was raised under.
            code.RateOn(postingDate) ?? 0m,
            direction is TaxDirection.Output ? code.OutputAccountNo : code.InputAccountNo,
            line.TaxIncludedInAmount);
    }

    /// <summary>
    /// The kind of party a whole journal is about, when it is about exactly one.
    /// </summary>
    /// <remarks>
    /// Null when a journal names none, or names both a customer and a vendor. Guessing between
    /// the two would put a figure on the wrong side of the return, which is worse than falling
    /// back to the sign.
    /// </remarks>
    private static PartyKind? DocumentPartyKind(
        IReadOnlyList<PostJournalLine> lines,
        IReadOnlyDictionary<(JournalAccountType Type, string No), PostingPartyView> parties)
    {
        var kinds = lines
            .Select(l => PartyFor(l, parties)?.Kind)
            .Where(static k => k is not null)
            .Distinct()
            .Take(2)
            .ToList();

        return kinds.Count == 1 ? kinds[0] : null;
    }

    private static string? TaxAccountFor(
        PostJournalLine line,
        IReadOnlyDictionary<string, TaxCode> taxCodes,
        TaxDirection direction)
        => string.IsNullOrWhiteSpace(line.TaxCode) || !taxCodes.TryGetValue(line.TaxCode, out var code)
            ? null
            : direction is TaxDirection.Output ? code.OutputAccountNo : code.InputAccountNo;

    /// <summary>
    /// The accounts tax can land on, as views the engine can post to.
    /// </summary>
    /// <remarks>
    /// Built with direct posting allowed. These are control accounts a person may not key by
    /// hand, and rightly so, but a generated tax line is not a person keying by hand -- it is the
    /// module that owns the account writing to it, which is exactly what the restriction exists
    /// to leave room for.
    /// </remarks>
    private static Dictionary<string, PostingAccountView> TaxAccounts(
        IReadOnlyDictionary<string, TaxCode> taxCodes,
        Dictionary<string, GlAccount> accounts,
        List<Guid> companyDimensionIds)
    {
        var resolved = new Dictionary<string, PostingAccountView>(StringComparer.OrdinalIgnoreCase);

        foreach (var accountNo in taxCodes.Values
                     .SelectMany(static c => new[] { c.OutputAccountNo, c.InputAccountNo })
                     .Where(static no => !string.IsNullOrWhiteSpace(no))
                     .Select(static no => no!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Resolve(accountNo, accounts, companyDimensionIds) is { } account)
            {
                resolved[accountNo] = account with { AllowsDirectPosting = true };
            }
        }

        return resolved;
    }

    private static PostingAccountView? Resolve(
        string? accountNo,
        Dictionary<string, GlAccount> accounts,
        List<Guid> companyDimensionIds)
    {
        if (string.IsNullOrWhiteSpace(accountNo))
        {
            return null;
        }

        // An account number that matches nothing resolves to null, and the validator reports it as
        // a missing account. Throwing here would lose every other problem in the batch.
        return accounts.TryGetValue(accountNo, out var account)
            ? PostingAccountView.From(account, companyDimensionIds.ToHashSet())
            : null;
    }

    /// <summary>
    /// Which of the overridable blocks this caller may push past.
    /// </summary>
    /// <remarks>
    /// Every permission named as an <c>OverridePermission</c> on a message the validator can raise
    /// has to appear here. A message that offers an override the handler never collects is worse
    /// than one that offers none: it tells the user to go and find somebody who can approve it,
    /// and that person then finds they cannot either.
    /// </remarks>
    private IReadOnlySet<string> HeldOverrides()
    {
        HashSet<string> candidates =
        [
            $"{FinanceModule.Id}.Period.Override",
            $"{FinanceModule.Id}.Account.Override",
            $"{FinanceModule.Id}.Party.Override",
        ];

        return candidates.Where(userContext.Has).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> BaseCurrencyAsync(CancellationToken cancellationToken)
        => await context.Companies
               .AsNoTracking()
               .Select(static c => c.BaseCurrencyCode)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false)
           ?? "SAR";
}
