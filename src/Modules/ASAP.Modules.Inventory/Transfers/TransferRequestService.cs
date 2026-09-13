using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Transfers;

/// <summary>One thing being asked for.</summary>
/// <param name="ItemNo">What is wanted.</param>
/// <param name="Quantity">How much.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="Note">Anything the branch wants to say about it.</param>
public readonly record struct TransferRequestLineRequest(
    string ItemNo,
    decimal Quantity,
    string? VariantCode = null,
    string? Note = null);

/// <summary>What is being agreed to on one line.</summary>
/// <param name="LineNo">The line.</param>
/// <param name="Quantity">How much of it is being sent. Never more than was asked for.</param>
/// <param name="Note">Why less, where it is less.</param>
public readonly record struct TransferApprovalLine(int LineNo, decimal Quantity, string? Note = null);

/// <summary>
/// A branch asking for stock, and whoever holds it deciding.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as a purchase requisition: raise, submit, agree or turn down, and
/// then produce the document that commits. A branch cannot help itself to a warehouse's stock any
/// more than a department can help itself to the company's money, and for the same reason — the
/// place holding the goods knows what else is promised out of them and the branch asking does not.
/// </para>
/// <para>
/// Two rules carry it. Nobody answers their own request, because an approval you can give
/// yourself is a checkbox. And an approval may give less than was asked for but never more: a
/// warehouse clearing its shelves into a branch that cannot sell them has moved a problem rather
/// than solved one.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="transfers">Raises the transfer an agreed request becomes.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the request number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who is asking, and who is answering.</param>
/// <param name="clock">Says what today is.</param>
/// <param name="logger">Records what was asked and agreed.</param>
public sealed class TransferRequestService(
    AsapDbContext context,
    TransferService transfers,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock,
    ILogger<TransferRequestService> logger)
{
    /// <summary>The requests, most recent first.</summary>
    /// <param name="status">One status, or null for all of them.</param>
    /// <param name="take">How many.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requests and their lines.</returns>
    public async Task<IReadOnlyList<TransferRequest>> ListAsync(
        TransferRequestStatus? status = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<TransferRequest>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        return await query
            .OrderByDescending(r => r.RequestDate)
            .ThenByDescending(r => r.No)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>One request and what is on it.</summary>
    /// <param name="no">The request number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or null.</returns>
    public Task<TransferRequest?> LoadAsync(string no, CancellationToken cancellationToken = default)
        => context.Set<TransferRequest>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == no, cancellationToken);

    /// <summary>
    /// Raises a request for stock from somewhere that has it.
    /// </summary>
    /// <param name="fromLocationCode">Where the goods are being asked from.</param>
    /// <param name="toLocationCode">Where they are wanted.</param>
    /// <param name="lines">What is wanted.</param>
    /// <param name="neededByDate">When by.</param>
    /// <param name="reason">Why, which is what whoever holds the stock reads.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or every reason it was refused.</returns>
    public async Task<Result<TransferRequest>> CreateAsync(
        string fromLocationCode,
        string toLocationCode,
        IReadOnlyList<TransferRequestLineRequest> lines,
        DateOnly? neededByDate = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var from = fromLocationCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var to = toLocationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var found = new List<AsapMessage>();
        var arguments = Args(("From", from), ("To", to), ("Location", from));

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            found.Add(messages.Render(InventoryMessages.TransferToSameLocation, arguments));
        }

        foreach (var code in new[] { from, to }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exists = await context.Set<Location>()
                .AsNoTracking()
                .AnyAsync(l => l.Code == code, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                found.Add(messages.Render(
                    InventoryMessages.LocationNotFound,
                    Args(("LocationCode", code))));
            }
        }

        if (lines.Count == 0)
        {
            found.Add(messages.Render(InventoryMessages.TransferRequestHasNoLines, arguments));
        }

        var itemNos = lines.Select(static l => l.ItemNo?.Trim().ToUpperInvariant() ?? string.Empty)
            .Distinct()
            .ToList();

        var items = (await context.Set<Item>()
                .AsNoTracking()
                .Where(i => itemNos.Contains(i.No))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(static i => i.No, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var itemNo = line.ItemNo?.Trim().ToUpperInvariant() ?? string.Empty;
            var target = MessageTarget.OnField($"Lines[{index + 1}]");

            var lineArguments = Args(
                ("LineNo", index + 1),
                ("ItemNo", itemNo),
                ("Quantity", line.Quantity));

            if (!items.ContainsKey(itemNo))
            {
                found.Add(messages.Render(InventoryMessages.ItemNotFound, lineArguments, target));
                continue;
            }

            if (line.Quantity <= 0m)
            {
                found.Add(messages.Render(
                    InventoryMessages.TransferRequestQuantityZero,
                    lineArguments,
                    target));
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<TransferRequest>.Failure(found);
        }

        var seriesCode = await setup
            .GetAsync<string>($"{InventoryModule.Id}.TransferRequest.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "TRANSFER-REQ";

        var today = clock.Today;

        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<TransferRequest>.FailureFrom(numbered);
        }

        var request = new TransferRequest
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            No = numbered.Value,
            FromLocationCode = from,
            ToLocationCode = to,
            RequestDate = today,
            NeededByDate = neededByDate,
            Status = TransferRequestStatus.Draft,
            Reason = reason?.Trim(),
            RequestedByUserId = user.UserId,
            RequestedByUserName = user.DisplayName ?? user.UserName,
        };

        context.Set<TransferRequest>().Add(request);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var lineNo = 0;

        foreach (var line in lines)
        {
            lineNo += 10;

            context.Set<TransferRequestLine>().Add(new TransferRequestLine
            {
                TenantId = request.TenantId,
                CompanyId = request.CompanyId,
                TransferRequestId = request.Id,
                LineNo = lineNo,
                ItemNo = line.ItemNo!.Trim().ToUpperInvariant(),
                VariantCode = line.VariantCode?.Trim().ToUpperInvariant(),
                QuantityRequested = line.Quantity,
                Note = line.Note?.Trim(),
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TransferRequest>.Success(await ReloadAsync(request.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sends the request to whoever holds the stock.</summary>
    /// <param name="no">The request number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be sent.</returns>
    public async Task<Result<TransferRequest>> SubmitAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var request = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(no);
        }

        if (!request.IsEditable)
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.TransferRequestNotEditable,
                Args(("RequestNo", no), ("Status", request.Status.ToString()))));
        }

        request.Status = TransferRequestStatus.Submitted;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TransferRequest>.Success(await ReloadAsync(request.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Agrees to a request, in whole or in part.
    /// </summary>
    /// <param name="no">The request number.</param>
    /// <param name="lines">
    /// What is being agreed to, or null to agree to everything as asked.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be agreed to.</returns>
    public async Task<Result<TransferRequest>> ApproveAsync(
        string no,
        IReadOnlyList<TransferApprovalLine>? lines = null,
        CancellationToken cancellationToken = default)
    {
        var request = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(no);
        }

        if (request.Status is not TransferRequestStatus.Submitted)
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.TransferRequestNotAwaiting,
                Args(("RequestNo", no), ("Status", request.Status.ToString()))));
        }

        // The rule the whole exercise turns on. Whoever holds the stock decides, and the branch
        // asking is not whoever holds the stock.
        if (request.RequestedByUserId is { } asker && asker == user.UserId)
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.CannotApproveYourOwnTransferRequest,
                Args(("RequestNo", no), ("RequestedBy", request.RequestedByUserName))));
        }

        var found = new List<AsapMessage>();
        var agreed = lines?.ToDictionary(static l => l.LineNo);

        foreach (var line in request.Lines.OrderBy(static l => l.LineNo))
        {
            var quantity = agreed is null
                ? line.QuantityRequested
                : agreed.TryGetValue(line.LineNo, out var given) ? given.Quantity : 0m;

            if (quantity > line.QuantityRequested)
            {
                found.Add(messages.Render(
                    InventoryMessages.ApprovedMoreThanRequested,
                    Args(
                        ("LineNo", line.LineNo),
                        ("ItemNo", line.ItemNo),
                        ("Quantity", line.QuantityRequested),
                        ("ApprovedQuantity", quantity)),
                    MessageTarget.OnField($"Lines[{line.LineNo}]")));
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<TransferRequest>.Failure(found);
        }

        foreach (var line in request.Lines)
        {
            if (agreed is null)
            {
                line.QuantityApproved = line.QuantityRequested;
                continue;
            }

            if (agreed.TryGetValue(line.LineNo, out var given))
            {
                line.QuantityApproved = Math.Max(0m, given.Quantity);

                if (given.Note is { Length: > 0 })
                {
                    line.Note = given.Note.Trim();
                }
            }
            else
            {
                line.QuantityApproved = 0m;
            }
        }

        request.Status = TransferRequestStatus.Approved;
        request.ApprovedByUserId = user.UserId;
        request.ApprovedByUserName = user.DisplayName ?? user.UserName;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Transfer request {RequestNo} agreed by {ApprovedBy}.",
            no,
            request.ApprovedByUserName);

        return Result<TransferRequest>.Success(await ReloadAsync(request.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Turns a request down, with a reason.</summary>
    /// <param name="no">The request number.</param>
    /// <param name="reason">Why, which the branch reads.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be turned down.</returns>
    public async Task<Result<TransferRequest>> RejectAsync(
        string no,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(no);
        }

        if (request.Status is not TransferRequestStatus.Submitted)
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.TransferRequestNotAwaiting,
                Args(("RequestNo", no), ("Status", request.Status.ToString()))));
        }

        if (request.RequestedByUserId is { } asker && asker == user.UserId)
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.CannotApproveYourOwnTransferRequest,
                Args(("RequestNo", no), ("RequestedBy", request.RequestedByUserName))));
        }

        request.Status = TransferRequestStatus.Rejected;
        request.RejectionReason = reason?.Trim();
        request.ApprovedByUserId = user.UserId;
        request.ApprovedByUserName = user.DisplayName ?? user.UserName;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TransferRequest>.Success(await ReloadAsync(request.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Turns what was agreed into a transfer.
    /// </summary>
    /// <remarks>
    /// Nothing has moved until now, and nothing was reserved: the goods could have been sold from
    /// under the request in the meantime. The transfer is what commits, so this is where the stock
    /// checks apply, and they apply there rather than being repeated here.
    /// </remarks>
    /// <param name="no">The request number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The transfer raised, or why it could not be.</returns>
    public async Task<Result<TransferOrder>> TransferAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var request = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return Result<TransferOrder>.Failure(messages.Render(
                InventoryMessages.TransferRequestNotFound,
                Args(("RequestNo", no))));
        }

        if (request.Status is not TransferRequestStatus.Approved || !request.HasOutstanding)
        {
            return Result<TransferOrder>.Failure(messages.Render(
                request.Status is TransferRequestStatus.Approved or TransferRequestStatus.Fulfilled
                    ? InventoryMessages.TransferRequestNothingOutstanding
                    : InventoryMessages.TransferRequestNotAwaiting,
                Args(("RequestNo", no), ("Status", request.Status.ToString()))));
        }

        var sending = request.Lines
            .Where(static l => l.OutstandingToTransfer > 0m)
            .OrderBy(static l => l.LineNo)
            .ToList();

        var created = await transfers
            .CreateAsync(
                request.FromLocationCode,
                request.ToLocationCode,
                [.. sending.Select(static l => new TransferLineRequest(l.ItemNo, l.OutstandingToTransfer))],
                $"{request.No} — {request.Reason}".TrimEnd(' ', '—'),
                request.NeededByDate,
                cancellationToken)
            .ConfigureAwait(false);

        if (created.Failed)
        {
            return created;
        }

        foreach (var line in sending)
        {
            line.QuantityTransferred += line.OutstandingToTransfer;
        }

        if (!request.Lines.Any(static l => l.OutstandingToTransfer > 0m))
        {
            request.Status = TransferRequestStatus.Fulfilled;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Transfer request {RequestNo} became transfer {TransferNo}.",
            no,
            created.Value.No);

        return created;
    }

    /// <summary>Withdraws a request that has produced nothing.</summary>
    /// <param name="no">The request number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be withdrawn.</returns>
    public async Task<Result<TransferRequest>> CancelAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var request = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(no);
        }

        if (request.Lines.Any(static l => l.QuantityTransferred > 0m))
        {
            return Result<TransferRequest>.Failure(messages.Render(
                InventoryMessages.TransferRequestNotEditable,
                Args(("RequestNo", no), ("Status", request.Status.ToString()))));
        }

        request.Status = TransferRequestStatus.Cancelled;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TransferRequest>.Success(await ReloadAsync(request.Id, cancellationToken).ConfigureAwait(false));
    }

    private Result<TransferRequest> NotFound(string no)
        => Result<TransferRequest>.Failure(messages.Render(
            InventoryMessages.TransferRequestNotFound,
            Args(("RequestNo", no))));

    private Task<TransferRequest?> TrackedAsync(string no, CancellationToken cancellationToken)
        => context.Set<TransferRequest>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == no, cancellationToken);

    private async Task<TransferRequest> ReloadAsync(Guid id, CancellationToken cancellationToken)
        => await context.Set<TransferRequest>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

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
