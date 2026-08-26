using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Posting;

/// <summary>
/// Reverses a posted transaction by posting its mirror image.
/// </summary>
/// <remarks>
/// The only correction a posted entry gets. Nothing edits or deletes a ledger row, so a mistake is
/// undone by posting the opposite and leaving both on the record. An accountant reading the
/// account later sees the error and the correction, which is the point: a trail that can be tidied
/// up is not a trail.
/// </remarks>
/// <param name="TransactionNo">The transaction to reverse.</param>
/// <param name="PostingDate">
/// When to report the reversal. Defaults to the date of the original, which is what keeps a
/// corrected month looking corrected rather than pushing the fix into the next one.
/// </param>
/// <param name="Reason">Why it is being reversed. Recorded on the entries and in the audit log.</param>
[RequiresPermission("Finance", "Entry", PermissionAction.Reverse)]
public sealed record ReverseTransactionCommand(
    long TransactionNo,
    DateOnly? PostingDate = null,
    string? Reason = null) : ICommand<PostingReceipt>;

/// <summary>Posts the mirror image of an existing transaction.</summary>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders messages.</param>
/// <param name="tenantContext">Supplies the company being posted in.</param>
/// <param name="userContext">Supplies who is reversing, for the audit trail.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="logger">Records reversals.</param>
public sealed class ReverseTransactionCommandHandler(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    ITransactionNumberAllocator transactionNumbers,
    ILogger<ReverseTransactionCommandHandler> logger)
    : IRequestHandler<ReverseTransactionCommand, Result<PostingReceipt>>
{
    /// <inheritdoc />
    public async Task<Result<PostingReceipt>> HandleAsync(
        ReverseTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var original = await context.Set<GlEntry>()
            .Where(e => e.TransactionNo == request.TransactionNo)
            .OrderBy(e => e.AccountNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (original.Count == 0)
        {
            return Result<PostingReceipt>.Failure(messages.Render(
                FinanceMessages.AlreadyReversed,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TransactionNo"] = request.TransactionNo,
                    ["ReversedOn"] = clock.Today,
                }));
        }

        // Reversing a reversal would double the correction rather than undo it, and the second
        // reversal would look identical to the first on the account.
        if (original.Exists(static e => e.IsReversed))
        {
            return Result<PostingReceipt>.Failure(messages.Render(
                FinanceMessages.AlreadyReversed,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TransactionNo"] = request.TransactionNo,
                    ["ReversedOn"] = original.Find(static e => e.IsReversed)?.ModifiedAtUtc?.Date
                                     ?? clock.UtcNow.Date,
                }));
        }

        var reversalDate = request.PostingDate ?? original[0].PostingDate;

        var calendar = await FiscalCalendar.LoadAsync(context, cancellationToken).ConfigureAwait(false);
        var status = calendar.Resolve(reversalDate);

        if (status.Availability is not PeriodAvailability.Open)
        {
            // The same period rules apply to a correction as to the entry it corrects. A reversal
            // that could land in a closed year would be a way round the year-end lock.
            var code = status.Availability switch
            {
                PeriodAvailability.YearClosed => FinanceMessages.YearClosed,
                PeriodAvailability.PeriodClosed => FinanceMessages.PeriodClosed,
                _ => FinanceMessages.NoOpenPeriod,
            };

            var refusal = messages.Render(
                code,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LineNo"] = 1,
                    ["PostingDate"] = reversalDate,
                    ["PeriodName"] = status.PeriodName,
                    ["FiscalYear"] = status.FiscalYearCode,
                });

            var mayOverride = refusal.OverridePermission is { } permission
                              && (userContext.IsSuperUser || userContext.Has(permission));

            if (!mayOverride)
            {
                return Result<PostingReceipt>.Failure(refusal);
            }
        }

        var transactionNo = await NextTransactionNoAsync(cancellationToken).ConfigureAwait(false);
        var reversals = new List<GlEntry>(original.Count);

        foreach (var entry in original)
        {
            var mirrored = -entry.Amount;
            var (debit, credit) = GlEntry.Split(mirrored);

            var reversal = new GlEntry
            {
                TenantId = entry.TenantId,
                CompanyId = entry.CompanyId,
                PostingDate = reversalDate,
                TransactionNo = transactionNo,
                AccountId = entry.AccountId,
                AccountNo = entry.AccountNo,
                DocumentType = entry.DocumentType,
                DocumentNo = entry.DocumentNo,
                Description = request.Reason is { Length: > 0 } reason
                    ? $"Reversal of {request.TransactionNo}: {reason}"
                    : $"Reversal of {request.TransactionNo}",
                Amount = mirrored,
                DebitAmount = debit,
                CreditAmount = credit,
                CurrencyCode = entry.CurrencyCode,
                AmountInCurrency = entry.AmountInCurrency is { } amount ? -amount : null,
                ExchangeRate = entry.ExchangeRate,
                DimensionSetId = entry.DimensionSetId,
                ShortcutDimension1Id = entry.ShortcutDimension1Id,
                ShortcutDimension2Id = entry.ShortcutDimension2Id,
                SourceCode = entry.SourceCode,
                BranchId = entry.BranchId,
                ReversalOfEntryId = entry.Id,
            };

            reversals.Add(reversal);

            // The original is not edited, only flagged. Its amounts, date and description stay
            // exactly as posted; all that changes is that it now points at its correction, so the
            // pair can be read from either end.
            entry.IsReversed = true;
            entry.ReversedByEntryId = reversal.Id;
        }

        context.Set<GlEntry>().AddRange(reversals);

        await ApplyBalancesAsync(reversals, cancellationToken).ConfigureAwait(false);

        context.AuditLog.Add(new AuditLogEntry
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.CompanyId,
            BranchId = tenantContext.BranchId,
            UserId = userContext.UserId,
            UserName = userContext.UserName,
            OccurredAtUtc = clock.UtcNow,
            Action = AuditAction.Reversed,
            EntityType = "Finance.GlEntry",
            DisplayNo = original[0].DocumentNo ?? request.TransactionNo.ToString(),
            OverrideReason = request.Reason,
            Changes = $"Transaction {request.TransactionNo} reversed by transaction {transactionNo}.",
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reversed transaction {Original} as transaction {Reversal}, {EntryCount} entries.",
            request.TransactionNo,
            transactionNo,
            reversals.Count);

        var receipt = new PostingReceipt(
            transactionNo,
            original[0].DocumentNo,
            reversals.Count,
            reversals.Sum(static e => e.DebitAmount));

        return Result<PostingReceipt>.Success(
            receipt,
            messages.Render(
                FinanceMessages.Posted,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EntryCount"] = reversals.Count,
                    ["TransactionNo"] = transactionNo,
                    ["DocumentNo"] = original[0].DocumentNo,
                }));
    }

    private async Task ApplyBalancesAsync(List<GlEntry> entries, CancellationToken cancellationToken)
    {
        var movements = entries
            .GroupBy(static e => e.AccountId)
            .ToDictionary(static g => g.Key, static g => g.Sum(static e => e.Amount));

        var accountIds = movements.Keys.ToList();

        var accounts = await context.Set<GlAccount>()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var account in accounts)
        {
            account.Balance += movements[account.Id];
        }
    }

    private Task<long> NextTransactionNoAsync(CancellationToken cancellationToken)
        => transactionNumbers.NextAsync(cancellationToken);
}
