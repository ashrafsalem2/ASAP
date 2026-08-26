using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
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
/// <param name="AccountNo">The account number, for example <c>6400</c>.</param>
/// <param name="Amount">The signed amount. Positive debits the account, negative credits it.</param>
/// <param name="Description">What the entry should say. Falls back to the account name.</param>
/// <param name="BalancingAccountNo">
/// What this line balances against. When given, the line stands alone and produces two entries.
/// </param>
/// <param name="PostingDate">The date to report the entry in. Defaults to today.</param>
public sealed record PostJournalLine(
    string AccountNo,
    decimal Amount,
    string? Description = null,
    string? BalancingAccountNo = null,
    DateOnly? PostingDate = null);

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

        var accountNumbers = request.Lines
            .SelectMany(static l => new[] { l.AccountNo, l.BalancingAccountNo })
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
            .Select((line, index) => new PostingLineView(
                LineNo: index + 1,
                PostingDate: line.PostingDate ?? today,
                Amount: line.Amount,
                Account: Resolve(line.AccountNo, accounts, companyDimensionIds),
                BalancingAccount: Resolve(line.BalancingAccountNo, accounts, companyDimensionIds),
                Dimensions: DimensionCombination.Empty,
                DocumentNo: request.DocumentNo,
                Description: line.Description))
            .ToList();

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
            IsManualEntry: true);

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

    private IReadOnlySet<string> HeldOverrides()
    {
        HashSet<string> candidates =
        [
            $"{FinanceModule.Id}.Period.Override",
            $"{FinanceModule.Id}.Account.Override",
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
