using ASAP.Api.Infrastructure;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Transfers;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>One line on a transfer, as a client sends it.</summary>
/// <param name="ItemNo">The item to move.</param>
/// <param name="Quantity">How much. Positive; the direction belongs to the transfer.</param>
public sealed record TransferLinePayload(string ItemNo, decimal Quantity);

/// <summary>What a client sends to raise a transfer.</summary>
/// <param name="FromLocationCode">Where the goods leave.</param>
/// <param name="ToLocationCode">Where they are going.</param>
/// <param name="Lines">What is moving.</param>
/// <param name="Description">A note for whoever handles it.</param>
/// <param name="ExpectedReceiptDate">When it should arrive.</param>
public sealed record CreateTransferRequest(
    string FromLocationCode,
    string ToLocationCode,
    IReadOnlyList<TransferLinePayload> Lines,
    string? Description = null,
    DateOnly? ExpectedReceiptDate = null);

/// <summary>What a client sends to receive a shipment.</summary>
/// <param name="Shortages">
/// Quantity actually received per item, where it differs from what was sent. Absent items are
/// taken as arriving in full.
/// </param>
/// <param name="OverrideReason">Why a protection is being pushed past, if one is.</param>
public sealed record ReceiveTransferRequest(
    IReadOnlyDictionary<string, decimal>? Shortages = null,
    string? OverrideReason = null);

/// <summary>One line of a transfer as it is reported back.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="Quantity">How much was asked for.</param>
/// <param name="QuantityShipped">How much has left.</param>
/// <param name="QuantityReceived">How much has arrived.</param>
/// <param name="InTransit">How much is still travelling.</param>
public sealed record TransferLineView(
    int LineNo,
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    decimal Quantity,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal InTransit);

/// <summary>A transfer as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="FromLocationCode">Where the goods leave.</param>
/// <param name="ToLocationCode">Where they are going.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="ShipmentDate">When it was raised.</param>
/// <param name="ExpectedReceiptDate">When it should arrive.</param>
/// <param name="ShippedOn">When it left.</param>
/// <param name="ReceivedOn">When it arrived.</param>
/// <param name="Description">The note on it.</param>
/// <param name="Lines">What is on it.</param>
public sealed record TransferView(
    string No,
    string FromLocationCode,
    string ToLocationCode,
    string Status,
    DateOnly ShipmentDate,
    DateOnly? ExpectedReceiptDate,
    DateOnly? ShippedOn,
    DateOnly? ReceivedOn,
    string? Description,
    IReadOnlyList<TransferLineView> Lines);

/// <summary>Raising, shipping and receiving stock transfers.</summary>
public static class TransferEndpoints
{
    private const string ReadPermission = "Inventory.Transfer.Read";
    private const string PostPermission = "Inventory.Transfer.Post";

    /// <summary>Maps the transfer endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/inventory/transfers")
                       .RequireAuthorization()
                       .WithTags("Inventory");

        group.MapGet("/", ListAsync)
             .WithName("Transfers")
             .WithSummary("Lists transfers, most recently raised first.");

        group.MapGet("/{transferNo}", GetAsync)
             .WithName("Transfer")
             .WithSummary("Reads one transfer and its lines.");

        group.MapPost("/", CreateAsync)
             .WithName("CreateTransfer")
             .WithSummary("Raises a transfer. Nothing moves until it is shipped.");

        group.MapPost("/{transferNo}/ship", ShipAsync)
             .WithName("ShipTransfer")
             .WithSummary("Sends the goods: out of the source and into transit.");

        group.MapPost("/{transferNo}/receive", ReceiveAsync)
             .WithName("ReceiveTransfer")
             .WithSummary("Lands the goods: out of transit and into the destination.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view transfers", http);
        }

        var query = context.Set<TransferOrder>().AsNoTracking().Include(t => t.Lines).AsQueryable();

        if (Enum.TryParse<TransferStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(t => t.Status == wanted);
        }

        var transfers = await query
            .OrderByDescending(t => t.No)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(transfers.Select(t => View(t, descriptions: null)));
    }

    private static async Task<IResult> GetAsync(
        string transferNo,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view transfers", http);
        }

        var transfer = await context.Set<TransferOrder>()
            .AsNoTracking()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.No == transferNo, cancellationToken)
            .ConfigureAwait(false);

        return transfer is null ? Results.NotFound() : Results.Ok(View(transfer, descriptions: null));
    }

    private static async Task<IResult> CreateAsync(
        CreateTransferRequest request,
        TransferService transfers,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, PostPermission))
        {
            return Forbidden(PostPermission, "raise transfers", http);
        }

        var result = await transfers
            .CreateAsync(
                request.FromLocationCode,
                request.ToLocationCode,
                [.. request.Lines.Select(l => new TransferLineRequest(l.ItemNo, l.Quantity))],
                request.Description,
                request.ExpectedReceiptDate,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Refused(result, http);
        }

        return Results.Ok(new
        {
            transfer = View(result.Value, descriptions: null),
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static Task<IResult> ShipAsync(
        string transferNo,
        TransferService transfers,
        ISetupService setup,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => MoveAsync(
            user,
            http,
            setup,
            (allowsNegative, overrides) =>
                transfers.ShipAsync(transferNo, allowsNegative, overrides, cancellationToken),
            cancellationToken);

    private static Task<IResult> ReceiveAsync(
        string transferNo,
        ReceiveTransferRequest? request,
        TransferService transfers,
        ISetupService setup,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => MoveAsync(
            user,
            http,
            setup,
            (allowsNegative, overrides) => transfers.ReceiveAsync(
                transferNo,
                request?.Shortages,
                allowsNegative,
                overrides,
                cancellationToken),
            cancellationToken);

    /// <summary>
    /// The half that shipping and receiving share: check the permission, read the negative-stock
    /// setting, collect the overrides actually held, run the move and report it.
    /// </summary>
    private static async Task<IResult> MoveAsync(
        IUserContext user,
        HttpContext http,
        ISetupService setup,
        Func<bool, IReadOnlySet<string>, Task<Result<TransferReceipt>>> move,
        CancellationToken cancellationToken)
    {
        if (!Can(user, PostPermission))
        {
            return Forbidden(PostPermission, "ship and receive transfers", http);
        }

        var allowsNegative = await setup
            .GetAsync<bool>($"{InventoryModule.Id}.Costing.AllowNegativeInventory", cancellationToken)
            .ConfigureAwait(false);

        var overrides = new[] { "Inventory.Stock.Override" }
            .Where(permission => Can(user, permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await move(allowsNegative, overrides).ConfigureAwait(false);

        if (result.Failed)
        {
            return Refused(result, http);
        }

        return Results.Ok(new
        {
            transferNo = result.Value.TransferNo,
            transactionNo = result.Value.TransactionNo,
            lineCount = result.Value.LineCount,
            status = result.Value.Status.ToString(),
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static TransferView View(TransferOrder transfer, IReadOnlyDictionary<string, string>? descriptions)
        => new(
            transfer.No,
            transfer.FromLocationCode,
            transfer.ToLocationCode,
            transfer.Status.ToString(),
            transfer.ShipmentDate,
            transfer.ExpectedReceiptDate,
            transfer.ShippedOn,
            transfer.ReceivedOn,
            transfer.Description,
            [.. transfer.Lines
                .OrderBy(static l => l.LineNo)
                .Select(l => new TransferLineView(
                    l.LineNo,
                    l.ItemNo,
                    l.Description,
                    descriptions?.GetValueOrDefault(l.ItemNo),
                    l.Quantity,
                    l.QuantityShipped,
                    l.QuantityReceived,
                    l.InTransit))]);

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
