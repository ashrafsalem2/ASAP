using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>What one provision run posted, and what the two provisions come to now.</summary>
/// <param name="AsOf">The day the run was measured at.</param>
/// <param name="EndOfServiceTotal">What the company owes in end-of-service, in total.</param>
/// <param name="EndOfServiceMovement">How much that changed since the last run.</param>
/// <param name="LeaveTotal">What the company owes in unused leave, in total.</param>
/// <param name="LeaveMovement">How much that changed since the last run.</param>
/// <param name="TransactionNo">
/// The transaction the ledger lines were posted under, or null when nothing had moved.
/// </param>
public readonly record struct ProvisionPostingSummary(
    DateOnly AsOf,
    decimal EndOfServiceTotal,
    decimal EndOfServiceMovement,
    decimal LeaveTotal,
    decimal LeaveMovement,
    long? TransactionNo);

/// <summary>
/// Moves what the company owes in end-of-service and unused leave into the general ledger.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="People.EmployeeService.EntitlementsAsync"/> already knows what the company owes
/// every current employee, computed the same way whether they leave today or stay another ten
/// years. What it does not do is post — a report is read on demand and costs nothing to be wrong
/// about for a minute; a ledger entry is a fact the moment it lands. This is the part that turns
/// the figure into one.
/// </para>
/// <para>
/// A provision has no settlement moment the way an invoice does, so there is nothing to post
/// except the change since last time. Run it monthly, run it twice in a day, run it the day after
/// somebody's wage rises — each run posts only the movement, and running it again with nothing
/// changed posts nothing at all.
/// </para>
/// <para>
/// Goes through the same <see cref="LedgerPostingRequested"/> kernel event every other module's
/// posting goes through, and for the same reason: HR knows the figure, Finance owns the ledger,
/// and neither references the other. HR depends on Finance at the module level — see
/// <see cref="HrModule.DependsOn"/> — so unlike a module that can run standalone, this always
/// finds something listening.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="employees">Computes what the company owes.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="setup">Supplies the posting accounts.</param>
/// <param name="events">Carries the posting request to whichever module owns the ledger.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records what moved and what did not.</param>
public sealed class ProvisionPostingService(
    AsapDbContext context,
    EmployeeService employees,
    IMessageCatalog messages,
    ISetupService setup,
    IEventPublisher events,
    ITransactionNumberAllocator transactionNumbers,
    IClock clock,
    ILogger<ProvisionPostingService> logger)
{
    /// <summary>
    /// Computes what the company owes today and posts however much that has moved.
    /// </summary>
    /// <param name="on">The day to measure at, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason it could not be.</returns>
    public async Task<Result<ProvisionPostingSummary>> PostAsync(
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var day = on ?? clock.Today;

        var entitlements = await employees.EntitlementsAsync(day, cancellationToken).ConfigureAwait(false);

        var endOfServiceTotal = Round(entitlements.Sum(static e => e.EndOfService));
        var leaveTotal = Round(entitlements.Sum(static e => e.LeaveLiability));

        var watermarks = await context.Set<EntitlementProvision>()
            .Where(p => p.Type == ProvisionType.EndOfService || p.Type == ProvisionType.Leave)
            .ToDictionaryAsync(static p => p.Type, cancellationToken)
            .ConfigureAwait(false);

        var found = new List<AsapMessage>();
        var lines = new List<LedgerPostingLine>();
        var updates = new List<(ProvisionType Type, decimal Total)>();

        await AddMovementAsync(
            ProvisionType.EndOfService,
            endOfServiceTotal,
            $"{HrModule.Id}.Posting.EndOfServiceExpenseAccount",
            $"{HrModule.Id}.Posting.EndOfServiceAccount",
            "End-of-service provision",
            watermarks,
            lines,
            found,
            updates,
            cancellationToken).ConfigureAwait(false);

        await AddMovementAsync(
            ProvisionType.Leave,
            leaveTotal,
            $"{HrModule.Id}.Posting.LeaveExpenseAccount",
            $"{HrModule.Id}.Posting.LeaveProvisionAccount",
            "Unused leave provision",
            watermarks,
            lines,
            found,
            updates,
            cancellationToken).ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<ProvisionPostingSummary>.Failure(found);
        }

        var endOfServiceMovement = endOfServiceTotal - (watermarks.TryGetValue(
            ProvisionType.EndOfService, out var eos) ? eos.PostedAmount : 0m);
        var leaveMovement = leaveTotal - (watermarks.TryGetValue(
            ProvisionType.Leave, out var leave) ? leave.PostedAmount : 0m);

        if (lines.Count == 0)
        {
            found.Add(messages.Render(
                HrMessages.NothingToProvision,
                Args(("Amount", endOfServiceTotal + leaveTotal))));

            return Result<ProvisionPostingSummary>.Success(
                new ProvisionPostingSummary(day, endOfServiceTotal, 0m, leaveTotal, 0m, null),
                found);
        }

        var transactionNo = await transactionNumbers.NextAsync(cancellationToken).ConfigureAwait(false);

        await events
            .PublishAsync(
                new LedgerPostingRequested
                {
                    SourceModule = HrModule.Id,
                    SourceCode = "HR-PROV",
                    PostingDate = day,
                    DocumentNo = null,
                    SourceTransactionNo = transactionNo,
                    Lines = lines,
                },
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var (type, total) in updates)
        {
            if (watermarks.TryGetValue(type, out var existing))
            {
                existing.PostedAmount = total;
                existing.AsOf = day;
                existing.LastTransactionNo = transactionNo;
            }
            else
            {
                // Tenant and company are stamped on save from the active context, the same as
                // every other new row -- see AsapDbContext's before-save fixup.
                context.Set<EntitlementProvision>().Add(new EntitlementProvision
                {
                    Type = type,
                    PostedAmount = total,
                    AsOf = day,
                    LastTransactionNo = transactionNo,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted entitlement provisions as transaction {TransactionNo}: end-of-service moved "
            + "{EndOfServiceMovement} to {EndOfServiceTotal}, leave moved {LeaveMovement} to "
            + "{LeaveTotal}.",
            transactionNo,
            endOfServiceMovement,
            endOfServiceTotal,
            leaveMovement,
            leaveTotal);

        return Result<ProvisionPostingSummary>.Success(
            new ProvisionPostingSummary(
                day, endOfServiceTotal, endOfServiceMovement, leaveTotal, leaveMovement, transactionNo),
            found);
    }

    /// <summary>
    /// Works out one provision's movement and adds the ledger lines for it, when it has one.
    /// </summary>
    private async Task AddMovementAsync(
        ProvisionType type,
        decimal total,
        string expenseAccountKey,
        string liabilityAccountKey,
        string description,
        IReadOnlyDictionary<ProvisionType, EntitlementProvision> watermarks,
        List<LedgerPostingLine> lines,
        List<AsapMessage> found,
        List<(ProvisionType Type, decimal Total)> updates,
        CancellationToken cancellationToken)
    {
        var posted = watermarks.TryGetValue(type, out var existing) ? existing.PostedAmount : 0m;
        var movement = Round(total - posted);

        updates.Add((type, total));

        if (movement == 0m)
        {
            return;
        }

        var expenseAccount = await setup
            .GetAsync<string>(expenseAccountKey, cancellationToken)
            .ConfigureAwait(false);
        var liabilityAccount = await setup
            .GetAsync<string>(liabilityAccountKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(expenseAccount))
        {
            found.Add(messages.Render(
                HrMessages.NoProvisionAccount,
                Args(("SettingKey", expenseAccountKey), ("Amount", movement))));
        }

        if (string.IsNullOrWhiteSpace(liabilityAccount))
        {
            found.Add(messages.Render(
                HrMessages.NoProvisionAccount,
                Args(("SettingKey", liabilityAccountKey), ("Amount", movement))));
        }

        if (string.IsNullOrWhiteSpace(expenseAccount) || string.IsNullOrWhiteSpace(liabilityAccount))
        {
            return;
        }

        // The expense side takes the movement as it stands: a growing provision debits it, a
        // shrinking one credits it back. The liability side is always the mirror image, which is
        // what makes the pair balance regardless of which way the movement runs.
        lines.Add(new LedgerPostingLine(expenseAccount, movement, description));
        lines.Add(new LedgerPostingLine(liabilityAccount, -movement, description));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

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
