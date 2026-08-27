using ASAP.Api.Infrastructure;
using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Sessions;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to open a till.</summary>
/// <param name="StationCode">The till to open.</param>
/// <param name="OpeningFloat">What is in the drawer to start with.</param>
public sealed record OpenSessionRequest(string StationCode, decimal OpeningFloat = 0m);

/// <summary>What a client sends to close a till.</summary>
/// <param name="DeclaredCash">What the cashier counted.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record CloseSessionRequest(decimal DeclaredCash, string? OverrideReason = null);

/// <summary>One thing being rung up, as a client sends it.</summary>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much. Negative takes goods back.</param>
/// <param name="UnitPrice">The price, or zero to take the item's own.</param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it says on the receipt.</param>
/// <param name="TaxCode">The tax to charge.</param>
public sealed record PosLinePayload(
    PosLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null);

/// <summary>Money put towards a receipt, as a client sends it.</summary>
/// <param name="Kind">What kind of money it is.</param>
/// <param name="Amount">How much was handed over, change included.</param>
/// <param name="Reference">Whatever identifies it afterwards.</param>
public sealed record PosTenderPayload(TenderKind Kind, decimal Amount, string? Reference = null);

/// <summary>What a client sends to ring up a sale.</summary>
/// <param name="Lines">What is being sold.</param>
/// <param name="Tenders">How it is being paid for.</param>
/// <param name="CustomerNo">Who to record it against, or null for the till's walk-in customer.</param>
/// <param name="ReturnsReceiptNo">The receipt being returned against, when there is one.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostReceiptRequest(
    IReadOnlyList<PosLinePayload> Lines,
    IReadOnlyList<PosTenderPayload> Tenders,
    string? CustomerNo = null,
    string? ReturnsReceiptNo = null,
    string? OverrideReason = null);

/// <summary>A till, as it is reported back.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="LocationCode">Where it sells from.</param>
/// <param name="DefaultCustomerNo">Who walk-in sales are recorded against.</param>
/// <param name="IsBlocked">Whether it is out of service.</param>
/// <param name="OpenSessionNo">The session open on it, if any.</param>
public sealed record PosStationView(
    string Code,
    string Name,
    string? NameArabic,
    string LocationCode,
    string DefaultCustomerNo,
    bool IsBlocked,
    string? OpenSessionNo);

/// <summary>Tills, sessions and receipts.</summary>
public static class PosEndpoints
{
    private const string StationReadPermission = "Pos.Station.Read";
    private const string SessionReadPermission = "Pos.Session.Read";
    private const string SessionOpenPermission = "Pos.Session.Create";
    private const string SessionClosePermission = "Pos.Session.Post";
    private const string ReceiptReadPermission = "Pos.Receipt.Read";
    private const string ReceiptPostPermission = "Pos.Receipt.Post";

    /// <summary>Maps the point of sale endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/pos").RequireAuthorization().WithTags("Point of sale");

        group.MapGet("/stations", StationsAsync)
             .WithName("PosStations")
             .WithSummary("Lists the tills, and says which of them are open.");

        group.MapGet("/sessions", SessionsAsync)
             .WithName("PosSessions")
             .WithSummary("Lists till sessions, most recently opened first.");

        group.MapGet("/sessions/{sessionNo}", SessionAsync)
             .WithName("PosSession")
             .WithSummary("Reads one session and its receipts.");

        group.MapPost("/sessions", OpenAsync)
             .WithName("OpenPosSession")
             .WithSummary("Opens a drawer at a till with a counted float.");

        group.MapGet("/sessions/{sessionNo}/reading", ReadingAsync)
             .WithName("PosXReading")
             .WithSummary("Reads a session without closing it, which is an X reading.");

        group.MapPost("/sessions/{sessionNo}/close", CloseAsync)
             .WithName("ClosePosSession")
             .WithSummary("Counts the drawer and finishes the session, which is a Z reading.");

        group.MapPost("/sessions/{sessionNo}/receipts", PostReceiptAsync)
             .WithName("PostPosReceipt")
             .WithSummary("Rings up a sale, takes the money and posts everything.");

        return app;
    }

    private static async Task<IResult> StationsAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, StationReadPermission))
        {
            return Forbidden(StationReadPermission, "view tills", http);
        }

        var stations = await context.Set<PosStation>()
            .AsNoTracking()
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Which till is open is the first thing anybody standing at one wants to know, so it
        // travels with the list rather than costing a second request per till.
        var open = await context.Set<PosSession>()
            .AsNoTracking()
            .Where(s => s.Status == PosSessionStatus.Open)
            .Select(static s => new { s.StationCode, s.No })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var openByStation = open.ToDictionary(
            static s => s.StationCode,
            static s => s.No,
            StringComparer.OrdinalIgnoreCase);

        return Results.Ok(stations.Select(s => new PosStationView(
            s.Code,
            s.Name,
            s.NameArabic,
            s.LocationCode,
            s.DefaultCustomerNo,
            s.IsBlocked,
            openByStation.GetValueOrDefault(s.Code))));
    }

    private static async Task<IResult> SessionsAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? stationCode,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SessionReadPermission))
        {
            return Forbidden(SessionReadPermission, "view till sessions", http);
        }

        var query = context.Set<PosSession>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(stationCode))
        {
            query = query.Where(s => s.StationCode == stationCode);
        }

        if (Enum.TryParse<PosSessionStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(s => s.Status == wanted);
        }

        var sessions = await query
            .OrderByDescending(s => s.OpenedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(sessions.Select(View));
    }

    private static async Task<IResult> SessionAsync(
        string sessionNo,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SessionReadPermission))
        {
            return Forbidden(SessionReadPermission, "view till sessions", http);
        }

        var session = await context.Set<PosSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.No == sessionNo, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Results.NotFound();
        }

        var receipts = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Where(r => r.SessionId == session.Id)
            .OrderByDescending(r => r.No)
            .Select(static r => new
            {
                r.No,
                r.CustomerName,
                r.NetAmount,
                r.TaxAmount,
                r.RoundingAmount,
                r.CostAmount,
                r.ChangeGiven,
                Status = r.Status.ToString(),
                r.TransactionNo,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { session = View(session), receipts });
    }

    private static async Task<IResult> OpenAsync(
        OpenSessionRequest request,
        PosSessionService sessions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SessionOpenPermission))
        {
            return Forbidden(SessionOpenPermission, "open a till", http);
        }

        var result = await sessions
            .OpenAsync(request.StationCode, request.OpeningFloat, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> ReadingAsync(
        string sessionNo,
        PosSessionService sessions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SessionReadPermission))
        {
            return Forbidden(SessionReadPermission, "read a till", http);
        }

        var result = await sessions.ReadAsync(sessionNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> CloseAsync(
        string sessionNo,
        CloseSessionRequest request,
        PosSessionService sessions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SessionClosePermission))
        {
            return Forbidden(SessionClosePermission, "close a till", http);
        }

        var result = await sessions
            .CloseAsync(
                sessionNo,
                request.DeclaredCash,
                Overrides(user),
                request.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                sessionNo = result.Value.SessionNo,
                expectedCash = result.Value.ExpectedCash,
                declaredCash = result.Value.DeclaredCash,
                variance = result.Value.Variance,
                transactionNo = result.Value.TransactionNo,
                reading = result.Value.Reading,
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> PostReceiptAsync(
        string sessionNo,
        PostReceiptRequest request,
        PosReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ReceiptPostPermission))
        {
            return Forbidden(ReceiptPostPermission, "take payment", http);
        }

        var result = await receipts
            .PostAsync(
                sessionNo,
                [.. request.Lines.Select(l => new PosLineRequest(
                    l.Type,
                    l.No,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.Description,
                    l.TaxCode))],
                [.. request.Tenders.Select(t => new PosTenderRequest(t.Kind, t.Amount, t.Reference))],
                request.CustomerNo,
                request.ReturnsReceiptNo,
                Overrides(user),
                request.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                receiptNo = result.Value.ReceiptNo,
                transactionNo = result.Value.TransactionNo,
                netAmount = result.Value.NetAmount,
                discountAmount = result.Value.DiscountAmount,
                taxAmount = result.Value.TaxAmount,
                roundingAmount = result.Value.RoundingAmount,
                totalAmount = result.Value.TotalAmount,
                changeGiven = result.Value.ChangeGiven,
                costAmount = result.Value.CostAmount,
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static object View(PosSession session)
        => new
        {
            no = session.No,
            stationCode = session.StationCode,
            cashierName = session.CashierName,
            openedAtUtc = session.OpenedAtUtc,
            businessDate = session.BusinessDate,
            status = session.Status.ToString(),
            openingFloat = session.OpeningFloat,
            cashTendered = session.CashTendered,
            changeGiven = session.ChangeGiven,
            cashRefunded = session.CashRefunded,
            cardTaken = session.CardTaken,
            onAccountTaken = session.OnAccountTaken,
            netSales = session.NetSales,
            taxAmount = session.TaxAmount,
            grossSales = session.GrossSales,
            receiptCount = session.ReceiptCount,
            readingCount = session.ReadingCount,
            expectedCash = session.ExpectedCash,
            declaredCash = session.DeclaredCash,
            variance = session.Variance,
            closedAtUtc = session.ClosedAtUtc,
            closingTransactionNo = session.ClosingTransactionNo,
        };

    /// <summary>
    /// The overrides this caller holds.
    /// </summary>
    /// <remarks>
    /// A receipt runs through Inventory's posting engine and Finance's, so a sale at a till can
    /// meet rules belonging to three modules besides this one.
    /// </remarks>
    private static IReadOnlySet<string> Overrides(IUserContext user)
        => new[]
           {
               "Pos.Receipt.Override",
               "Pos.Session.Override",
               "Inventory.Stock.Override",
               "Finance.Party.Override",
           }
            .Where(permission => Can(user, permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(Platform.Kernel.Results.Result result, HttpContext http)
        => Results.Json(
            AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
            statusCode: AsapProblem.StatusFor(result.Messages));
}
