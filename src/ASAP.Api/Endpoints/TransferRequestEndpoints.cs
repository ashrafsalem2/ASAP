using ASAP.Api.Infrastructure;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Transfers;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// The create payload in TransferEndpoints is also called TransferRequest; this file means the entity.
using RequestEntity = ASAP.Modules.Inventory.Transfers.TransferRequest;

namespace ASAP.Api.Endpoints;

/// <summary>One thing asked for, as it is reported back.</summary>
/// <param name="LineNo">Position on the request.</param>
/// <param name="ItemNo">What is wanted.</param>
/// <param name="ItemName">What it is called.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="QuantityRequested">How much the branch asked for.</param>
/// <param name="QuantityApproved">How much was agreed.</param>
/// <param name="QuantityTransferred">How much has gone onto a transfer.</param>
/// <param name="OutstandingToTransfer">What is agreed and not yet sent.</param>
/// <param name="Note">A note from either side.</param>
public sealed record TransferRequestLineView(
    int LineNo,
    string ItemNo,
    string? ItemName,
    string? VariantCode,
    decimal QuantityRequested,
    decimal QuantityApproved,
    decimal QuantityTransferred,
    decimal OutstandingToTransfer,
    string? Note);

/// <summary>A branch asking for stock, as it is reported back.</summary>
/// <param name="No">The request number.</param>
/// <param name="FromLocationCode">Where the goods are asked from.</param>
/// <param name="ToLocationCode">Where they are wanted.</param>
/// <param name="RequestDate">When it was raised.</param>
/// <param name="NeededByDate">When they are wanted by.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Reason">Why the branch wants them.</param>
/// <param name="RequestedByUserName">Who asked.</param>
/// <param name="ApprovedByUserName">Who answered.</param>
/// <param name="RejectionReason">Why it was turned down, where it was.</param>
/// <param name="HasOutstanding">Whether anything agreed is still to be sent.</param>
/// <param name="Lines">What is being asked for.</param>
public sealed record TransferRequestView(
    string No,
    string FromLocationCode,
    string ToLocationCode,
    DateOnly RequestDate,
    DateOnly? NeededByDate,
    TransferRequestStatus Status,
    string? Reason,
    string? RequestedByUserName,
    string? ApprovedByUserName,
    string? RejectionReason,
    bool HasOutstanding,
    IReadOnlyList<TransferRequestLineView> Lines);

/// <summary>What a branch sends to ask for stock.</summary>
/// <param name="FromLocationCode">Where the goods are asked from.</param>
/// <param name="ToLocationCode">Where they are wanted.</param>
/// <param name="Lines">What is wanted.</param>
/// <param name="NeededByDate">When by.</param>
/// <param name="Reason">Why, which is what whoever holds the stock reads.</param>
public sealed record CreateTransferRequestRequest(
    string FromLocationCode,
    string ToLocationCode,
    IReadOnlyList<TransferRequestLineRequest> Lines,
    DateOnly? NeededByDate = null,
    string? Reason = null);

/// <summary>What whoever holds the stock sends to agree to a request.</summary>
/// <param name="Lines">What is agreed to, or null to agree to everything as asked.</param>
public sealed record ApproveTransferRequestRequest(IReadOnlyList<TransferApprovalLine>? Lines = null);

/// <summary>What whoever holds the stock sends to turn a request down.</summary>
/// <param name="Reason">Why, which the branch reads.</param>
public sealed record RejectTransferRequestRequest(string Reason);

/// <summary>A branch asking for stock, and whoever holds it deciding.</summary>
public static class TransferRequestEndpoints
{
    private const string ReadPermission = "Inventory.Transfer.Read";
    private const string AskPermission = "Inventory.TransferRequest.Create";

    // Answering is the permission that ships stock, because agreeing to a request is agreeing to
    // send it. A branch that could ask and answer with one permission could help itself.
    private const string AnswerPermission = "Inventory.Transfer.Post";

    /// <summary>Maps the transfer request endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTransferRequestEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/inventory/transfer-requests")
                       .RequireAuthorization()
                       .WithTags("Inventory");

        group.MapGet("/", ListAsync)
             .WithName("TransferRequests")
             .WithSummary("Requests for stock, most recent first.");

        group.MapGet("/{requestNo}", GetAsync)
             .WithName("TransferRequest")
             .WithSummary("One request and what is on it.");

        group.MapPost("/", CreateAsync)
             .WithName("CreateTransferRequest")
             .WithSummary("Asks for stock from somewhere that has it. Commits nothing.");

        group.MapPost("/{requestNo}/submit", SubmitAsync)
             .WithName("SubmitTransferRequest")
             .WithSummary("Sends the request to whoever holds the stock.");

        group.MapPost("/{requestNo}/approve", ApproveAsync)
             .WithName("ApproveTransferRequest")
             .WithSummary("Agrees to a request, in whole or in part. Never more than was asked.");

        group.MapPost("/{requestNo}/reject", RejectAsync)
             .WithName("RejectTransferRequest")
             .WithSummary("Turns a request down, with a reason.");

        group.MapPost("/{requestNo}/transfer", TransferAsync)
             .WithName("TransferFromRequest")
             .WithSummary("Turns what was agreed into a transfer.");

        group.MapPost("/{requestNo}/cancel", CancelAsync)
             .WithName("CancelTransferRequest")
             .WithSummary("Withdraws a request that has produced nothing.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] TransferRequestStatus? status = null,
        [FromQuery] int take = 50)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view transfer requests", http);
        }

        var rows = await requests.ListAsync(status, take, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(rows, context, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(r => View(r, names)));
    }

    private static async Task<IResult> GetAsync(
        string requestNo,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view transfer requests", http);
        }

        var request = await requests.LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return Results.NotFound();
        }

        var names = await NamesAsync([request], context, cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(request, names));
    }

    private static async Task<IResult> CreateAsync(
        CreateTransferRequestRequest body,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Can(user, AskPermission))
        {
            return Forbidden(AskPermission, "ask for stock", http);
        }

        var result = await requests
            .CreateAsync(
                body.FromLocationCode,
                body.ToLocationCode,
                body.Lines ?? [],
                body.NeededByDate,
                body.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        return await RespondAsync(result, context, http, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> SubmitAsync(
        string requestNo,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, AskPermission))
        {
            return Forbidden(AskPermission, "send a transfer request", http);
        }

        var result = await requests.SubmitAsync(requestNo, cancellationToken).ConfigureAwait(false);

        return await RespondAsync(result, context, http, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> ApproveAsync(
        string requestNo,
        ApproveTransferRequestRequest? body,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, AnswerPermission))
        {
            return Forbidden(AnswerPermission, "agree to a transfer request", http);
        }

        var result = await requests
            .ApproveAsync(requestNo, body?.Lines, cancellationToken)
            .ConfigureAwait(false);

        return await RespondAsync(result, context, http, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> RejectAsync(
        string requestNo,
        RejectTransferRequestRequest body,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Can(user, AnswerPermission))
        {
            return Forbidden(AnswerPermission, "turn down a transfer request", http);
        }

        var result = await requests
            .RejectAsync(requestNo, body.Reason, cancellationToken)
            .ConfigureAwait(false);

        return await RespondAsync(result, context, http, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> TransferAsync(
        string requestNo,
        TransferRequestService requests,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, AnswerPermission))
        {
            return Forbidden(AnswerPermission, "send stock against a request", http);
        }

        var result = await requests.TransferAsync(requestNo, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                transferNo = result.Value.No,
                fromLocationCode = result.Value.FromLocationCode,
                toLocationCode = result.Value.ToLocationCode,
                lineCount = result.Value.Lines.Count,
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> CancelAsync(
        string requestNo,
        TransferRequestService requests,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, AskPermission))
        {
            return Forbidden(AskPermission, "withdraw a transfer request", http);
        }

        var result = await requests.CancelAsync(requestNo, cancellationToken).ConfigureAwait(false);

        return await RespondAsync(result, context, http, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> RespondAsync(
        Result<RequestEntity> result,
        AsapDbContext context,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (result.Failed)
        {
            return Refused(result, http);
        }

        var names = await NamesAsync([result.Value], context, cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(result.Value, names));
    }

    private static async Task<Dictionary<string, string>> NamesAsync(
        IReadOnlyList<RequestEntity> requests,
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var itemNos = requests.SelectMany(static r => r.Lines).Select(static l => l.ItemNo).Distinct().ToList();

        return await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, static i => i.Description, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);
    }

    private static TransferRequestView View(RequestEntity request, IReadOnlyDictionary<string, string> names)
        => new(
            request.No,
            request.FromLocationCode,
            request.ToLocationCode,
            request.RequestDate,
            request.NeededByDate,
            request.Status,
            request.Reason,
            request.RequestedByUserName,
            request.ApprovedByUserName,
            request.RejectionReason,
            request.HasOutstanding,
            [.. request.Lines
                .OrderBy(static l => l.LineNo)
                .Select(l => new TransferRequestLineView(
                    l.LineNo,
                    l.ItemNo,
                    names.GetValueOrDefault(l.ItemNo),
                    l.VariantCode,
                    l.QuantityRequested,
                    l.QuantityApproved,
                    l.QuantityTransferred,
                    l.OutstandingToTransfer,
                    l.Note))]);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(Result result, HttpContext http)
        => Results.Json(
            AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
            statusCode: AsapProblem.StatusFor(result.Messages));
}
