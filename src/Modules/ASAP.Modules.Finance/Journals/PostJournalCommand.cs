using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Tax;
using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Journals;

/// <summary>One line of a journal being posted through the API.</summary>
/// <param name="AccountNo">
/// What the line posts to. A general ledger account number such as <c>6400</c>, or a customer or
/// vendor number when <paramref name="AccountType"/> says so.
/// </param>
/// <param name="Amount">The signed amount. Positive debits the account, negative credits it.</param>
/// <param name="Description">What the entry should say. Falls back to the account name.</param>
/// <param name="BalancingAccountNo">
/// What this line balances against. When given, the line stands alone and produces two entries.
/// </param>
/// <param name="PostingDate">The date to report the entry in. Defaults to today.</param>
/// <param name="AccountType">
/// Whether the line posts to a general ledger account or to a customer or vendor. Modelled on the
/// line rather than the journal so one batch can hold an invoice and its contra, which is how
/// anybody actually keys a purchase day book.
/// </param>
/// <param name="ExternalDocumentNo">
/// The other side's reference, such as the number printed on the vendor's own invoice. Carried on
/// the party entry, where it is the thing people search by when a supplier telephones.
/// </param>
/// <param name="TaxCode">
/// The tax to apply, or null for a line carrying none. ASAP works out the tax and posts it beside
/// the line, so the person keying it never has to.
/// </param>
/// <param name="TaxIncludedInAmount">
/// Whether <paramref name="Amount"/> already contains the tax. False for a net figure the tax is
/// added to; true for a shelf price the tax comes out of.
/// </param>
public sealed record PostJournalLine(
    string AccountNo,
    decimal Amount,
    string? Description = null,
    string? BalancingAccountNo = null,
    DateOnly? PostingDate = null,
    JournalAccountType AccountType = JournalAccountType.GlAccount,
    string? ExternalDocumentNo = null,
    string? TaxCode = null,
    bool TaxIncludedInAmount = false);

/// <summary>
/// Posts a set of journal lines to the general ledger.
/// </summary>
/// <remarks>
/// Guarded by <c>Finance.Journal.Post</c>, which is deliberately distinct from the permission to
/// prepare a journal: the clerk who keys one is usually not the person who commits it.
/// </remarks>
/// <param name="BatchCode">The batch being posted, used in messages.</param>
/// <param name="Lines">The lines to post.</param>
/// <param name="DocumentNo">The document number the entries carry.</param>
/// <param name="Description">Default description for lines that supply none.</param>
/// <param name="OverrideReason">
/// Why the user is pushing past a block. Recorded in the audit log alongside the code overridden.
/// </param>
[RequiresPermission("Finance", "Journal", PermissionAction.Post)]
public sealed record PostJournalCommand(
    string BatchCode,
    IReadOnlyList<PostJournalLine> Lines,
    string? DocumentNo = null,
    string? Description = null,
    string? OverrideReason = null) : ICommand<PostingReceipt>;

/// <summary>
/// Resolves what a journal names into what the posting engine needs, then posts it.
/// </summary>
/// <remarks>
/// The handler's whole job is translation. It turns account numbers into accounts, reads the
/// calendar and the posting window, works out which blocks this caller may override, and hands a
/// fully resolved picture to the posting service. Every actual rule lives in the validator, where
/// it can be tested without any of this.
/// </remarks>
public sealed class PostJournalCommandHandler(
    AsapDbContext context,
    JournalPostingService posting,
    ISetupService setup,
    IMessageCatalog messages,
    IUserContext userContext,
    IClock clock) : IRequestHandler<PostJournalCommand, Result<PostingReceipt>>
{
    /// <inheritdoc />
    public async Task<Result<PostingReceipt>> HandleAsync(
        PostJournalCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = clock.Today;

        var parties = await ResolvePartiesAsync(request.Lines, cancellationToken).ConfigureAwait(false);
        var taxCodes = await ResolveTaxCodesAsync(request.Lines, cancellationToken).ConfigureAwait(false);

        // A party line posts to its control account, so that account has to be loaded alongside
        // the ones the journal names directly.
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
                    Tax: TaxFor(line, party, taxCodes, line.PostingDate ?? today));
            })
            .ToList();

        // A party number matching nothing is reported here rather than inside the validator, which
        // never sees the number the user typed -- only the party it failed to resolve to.
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
            IsManualEntry: true,

            // Every account a generated tax line might land on. The tax accounts are control
            // accounts, so this is also what lets a tax line reach one without an override.
            TaxAccountsByNo: TaxAccounts(taxCodes, accounts, companyDimensionIds));

        var postingRequest = new PostingRequest(
            SourceCode: "GENJNL",
            DocumentType: GlDocumentType.None,
            DocumentNo: request.DocumentNo,
            Description: request.Description,
            OverrideReason: request.OverrideReason);

        return await posting
            .PostAsync(lines, environment, postingRequest, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every customer and vendor the journal names, keyed by kind and number.
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

    /// <summary>Loads every tax code the journal names, with its rates.</summary>
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
        PostingPartyView? party,
        IReadOnlyDictionary<string, TaxCode> taxCodes,
        DateOnly postingDate)
    {
        if (string.IsNullOrWhiteSpace(line.TaxCode)
            || !taxCodes.TryGetValue(line.TaxCode, out var code))
        {
            return null;
        }

        var direction = party?.Kind switch
        {
            PartyKind.Customer => TaxDirection.Output,
            PartyKind.Vendor => TaxDirection.Input,
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
