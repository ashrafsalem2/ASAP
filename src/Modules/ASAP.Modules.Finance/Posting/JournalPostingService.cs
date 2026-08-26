using ASAP.Modules.Finance.Events;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Posting;

/// <summary>What a successful posting produced.</summary>
/// <param name="TransactionNo">The number grouping every entry written.</param>
/// <param name="DocumentNo">The document number the entries carry.</param>
/// <param name="EntryCount">How many entries were written.</param>
/// <param name="TotalAmount">The debit total, which equals the credit total.</param>
public readonly record struct PostingReceipt(
    long TransactionNo,
    string? DocumentNo,
    int EntryCount,
    decimal TotalAmount);

/// <summary>
/// Writes validated journal lines to the general ledger.
/// </summary>
/// <remarks>
/// <para>
/// The order of work here is not arbitrary. Validation runs first because a refusal should cost
/// nothing. Extensions get their say next, on lines that are already known to be sound. Only then
/// does anything touch the ledger, and everything after that point is inside one transaction: the
/// entries, the account balances, the audit record and the outbox row either all commit or none do.
/// </para>
/// <para>
/// Nothing here updates a posted entry. The service only ever inserts, which is what makes the
/// ledger a record rather than a working document.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="validator">Checks the lines before anything is written.</param>
/// <param name="events">Gives extensions their say, and announces the result.</param>
/// <param name="messages">Renders messages.</param>
/// <param name="tenantContext">Supplies the company and branch being posted in.</param>
/// <param name="userContext">Supplies who is posting, for the audit trail.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records postings and refusals.</param>
public sealed class JournalPostingService(
    AsapDbContext context,
    JournalPostingValidator validator,
    IEventPublisher events,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    ILogger<JournalPostingService> logger)
{
    /// <summary>
    /// Posts a set of lines to the general ledger.
    /// </summary>
    /// <param name="lines">The lines to post, with their accounts resolved.</param>
    /// <param name="environment">The calendar, posting window and override permissions.</param>
    /// <param name="request">What the entries should say about themselves.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A receipt on success, carrying any warnings raised along the way, or a failure carrying
    /// every reason the posting was refused.
    /// </returns>
    public async Task<Result<PostingReceipt>> PostAsync(
        IReadOnlyList<PostingLineView> lines,
        PostingEnvironment environment,
        PostingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(request);

        var validation = validator.Validate(lines, environment);

        if (validation.Failed)
        {
            logger.LogInformation(
                "Posting of batch {Batch} refused: {Codes}",
                environment.BatchCode,
                string.Join(", ", validation.Failures.Select(static m => m.Code.Value)));

            return Result<PostingReceipt>.FailureFrom(validation);
        }

        // Extensions object here, on lines already known to be sound, so a subscriber can
        // concentrate on its own rule rather than re-checking what ASAP has already checked.
        var posting = new JournalPosting
        {
            BatchCode = environment.BatchCode,
            Lines = lines,
            DocumentNo = request.DocumentNo,
            PostingDate = lines[0].PostingDate,
            TotalDebit = lines.Where(static l => l.Amount > 0).Sum(static l => l.Amount),
        };

        var vetoed = await events.PublishVetoableAsync(posting, cancellationToken).ConfigureAwait(false);

        if (vetoed.Failed)
        {
            logger.LogInformation(
                "Posting of batch {Batch} refused by an extension: {Codes}",
                environment.BatchCode,
                string.Join(", ", vetoed.Failures.Select(static m => m.Code.Value)));

            return Result<PostingReceipt>.FailureFrom(vetoed);
        }

        var transactionNo = await NextTransactionNoAsync(cancellationToken).ConfigureAwait(false);
        var entries = BuildEntries(lines, request, transactionNo, environment);

        context.Set<GlEntry>().AddRange(entries);

        await ApplyBalancesAsync(entries, cancellationToken).ConfigureAwait(false);

        // Warnings that started life as blocks record an override. This is the whole reason the
        // audit log carries a message code: "show me every time someone posted into a closed
        // period last quarter" becomes one indexed query.
        RecordOverrides(validation, vetoed, request, transactionNo);

        var totalAmount = entries.Sum(static e => e.DebitAmount);

        events.Enqueue(new JournalPosted
        {
            OccurredAtUtc = clock.UtcNow,
            TransactionNo = transactionNo,
            DocumentNo = request.DocumentNo,
            PostingDate = lines[0].PostingDate,
            EntryCount = entries.Count,
            TotalAmount = totalAmount,
            SourceCode = request.SourceCode,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted {EntryCount} entries as transaction {TransactionNo}, document {DocumentNo}.",
            entries.Count,
            transactionNo,
            request.DocumentNo);

        var receipt = new PostingReceipt(transactionNo, request.DocumentNo, entries.Count, totalAmount);

        var confirmation = messages.Render(
            FinanceMessages.Posted,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["EntryCount"] = entries.Count,
                ["TransactionNo"] = transactionNo,
                ["DocumentNo"] = request.DocumentNo,
            });

        // Warnings travel back with the success. A posting that went through while flagging that
        // it used an override should say so on the screen, not only in the log.
        return Result<PostingReceipt>.Success(
            receipt,
            [.. validation.Messages, .. vetoed.Messages, confirmation]);
    }

    /// <summary>
    /// Turns each line into the entries it produces.
    /// </summary>
    /// <remarks>
    /// A line with a balancing account produces two entries with opposite signs; one without
    /// produces a single entry and relies on its siblings to balance the batch.
    /// </remarks>
    private List<GlEntry> BuildEntries(
        IReadOnlyList<PostingLineView> lines,
        PostingRequest request,
        long transactionNo,
        PostingEnvironment environment)
    {
        var entries = new List<GlEntry>(lines.Count * 2);

        foreach (var line in lines)
        {
            var amount = Math.Round(line.Amount, environment.CurrencyDecimals, MidpointRounding.AwayFromZero);

            entries.Add(NewEntry(line, line.Account!, amount, request, transactionNo));

            if (line.BalancingAccount is { } balancing)
            {
                entries.Add(NewEntry(line, balancing, -amount, request, transactionNo));
            }
        }

        return entries;
    }

    private GlEntry NewEntry(
        PostingLineView line,
        PostingAccountView account,
        decimal amount,
        PostingRequest request,
        long transactionNo)
    {
        var (debit, credit) = GlEntry.Split(amount);

        return new GlEntry
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            PostingDate = line.PostingDate,
            TransactionNo = transactionNo,
            AccountId = account.Id,

            // Copied rather than joined, so a ledger report needs no join and history survives
            // the account being renumbered.
            AccountNo = account.No,
            DocumentType = request.DocumentType,
            DocumentNo = request.DocumentNo,
            Description = line.Description ?? request.Description ?? account.Name,
            Amount = amount,
            DebitAmount = debit,
            CreditAmount = credit,
            DimensionSetId = request.DimensionSetId,
            ShortcutDimension1Id = request.ShortcutDimension1Id ?? FirstShortcut(line.Dimensions, request, 1),
            ShortcutDimension2Id = request.ShortcutDimension2Id ?? FirstShortcut(line.Dimensions, request, 2),
            SourceCode = request.SourceCode,
            BranchId = tenantContext.BranchId,
        };
    }

    private static Guid? FirstShortcut(DimensionCombination dimensions, PostingRequest request, int position)
    {
        var shortcutId = position == 1 ? request.ShortcutDimension1DefinitionId : request.ShortcutDimension2DefinitionId;

        return shortcutId is { } id ? dimensions.ValueOf(id) : null;
    }

    /// <summary>
    /// Moves the running balance on every account the posting touched.
    /// </summary>
    /// <remarks>
    /// Updated inside the same transaction as the entries, so a balance cannot drift from the
    /// rows behind it. Denormalised deliberately: summing a million ledger rows to draw a chart
    /// of accounts is the difference between a screen that opens and one that times out.
    /// </remarks>
    private async Task ApplyBalancesAsync(List<GlEntry> entries, CancellationToken cancellationToken)
    {
        var movements = entries
            .GroupBy(static e => e.AccountId)
            .ToDictionary(static g => g.Key, static g => g.Sum(static e => e.Amount));

        var accountIds = movements.Keys.ToList();

        var accounts = await context.Set<Accounts.GlAccount>()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var account in accounts)
        {
            account.Balance += movements[account.Id];
        }
    }

    private void RecordOverrides(
        Result validation,
        Result vetoed,
        PostingRequest request,
        long transactionNo)
    {
        foreach (var warning in validation.Messages.Concat(vetoed.Messages))
        {
            // A warning carrying an override permission is one that was a block until this caller
            // turned out to hold the permission for it.
            if (warning.Severity is not MessageSeverity.Warning || warning.OverridePermission is null)
            {
                continue;
            }

            context.AuditLog.Add(new AuditLogEntry
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.CompanyId,
                BranchId = tenantContext.BranchId,
                UserId = userContext.UserId,
                UserName = userContext.UserName,
                OccurredAtUtc = clock.UtcNow,
                Action = AuditAction.Override,
                EntityType = "Finance.GlEntry",
                DisplayNo = request.DocumentNo ?? transactionNo.ToString(),
                OverriddenMessageCode = warning.Code.Value,
                OverrideReason = request.OverrideReason,
                Changes = warning.Detail,
            });
        }
    }

    /// <summary>
    /// Takes the next transaction number for the company.
    /// </summary>
    /// <remarks>
    /// A single atomic statement rather than a read followed by a write. Two tills posting at the
    /// same moment would otherwise both read the same last number and both claim the next one,
    /// producing two transactions sharing an identifier -- which nothing downstream could untangle,
    /// because the whole point of the number is that it groups one posting.
    /// </remarks>
    private async Task<long> NextTransactionNoAsync(CancellationToken cancellationToken)
    {
        var companyId = tenantContext.RequireCompanyId();

        var allocated = await context.Database
            .SqlQuery<long>(
                $@"UPDATE fin.TransactionCounters
                   SET LastTransactionNo = LastTransactionNo + 1
                   OUTPUT inserted.LastTransactionNo AS Value
                   WHERE CompanyId = {companyId}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (allocated.Count > 0)
        {
            return allocated[0];
        }

        // First posting in this company. Creating the row here rather than at company setup keeps
        // the counter an implementation detail of posting instead of something company creation
        // has to know about.
        context.Set<TransactionCounter>().Add(new TransactionCounter
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = companyId,
            LastTransactionNo = 1,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return 1;
    }
}

/// <summary>What the entries of a posting should say about themselves.</summary>
/// <param name="SourceCode">Where the posting came from, for example <c>GENJNL</c> or <c>POS</c>.</param>
/// <param name="DocumentType">What kind of document produced it.</param>
/// <param name="DocumentNo">The document number.</param>
/// <param name="Description">Default description, used where a line supplies none.</param>
/// <param name="DimensionSetId">The stored dimension combination the entries carry.</param>
/// <param name="ShortcutDimension1DefinitionId">Which dimension is shortcut 1, for copying its value onto entries.</param>
/// <param name="ShortcutDimension2DefinitionId">Which dimension is shortcut 2.</param>
/// <param name="ShortcutDimension1Id">An explicit shortcut 1 value, overriding what the line carries.</param>
/// <param name="ShortcutDimension2Id">An explicit shortcut 2 value.</param>
/// <param name="OverrideReason">Why the user pushed past a block, recorded in the audit log.</param>
public sealed record PostingRequest(
    string SourceCode,
    GlDocumentType DocumentType = GlDocumentType.None,
    string? DocumentNo = null,
    string? Description = null,
    Guid? DimensionSetId = null,
    Guid? ShortcutDimension1DefinitionId = null,
    Guid? ShortcutDimension2DefinitionId = null,
    Guid? ShortcutDimension1Id = null,
    Guid? ShortcutDimension2Id = null,
    string? OverrideReason = null);
