using ASAP.Api.Infrastructure;
using ASAP.Modules.Pos.Printing;
using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Reporting;
using ASAP.Modules.Pos.Sessions;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
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

/// <summary>Which bin a till sells off.</summary>
/// <param name="PickBinCode">The bin, or null where the location does not track them.</param>
public sealed record SetPickBinRequest(string? PickBinCode);

/// <summary>One thing being rung up, as a client sends it.</summary>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much. Negative takes goods back.</param>
/// <param name="UnitPrice">The price, or zero to take the item's own.</param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it says on the receipt.</param>
/// <param name="TaxCode">The tax to charge.</param>
/// <param name="UnitCode">The unit rung, or null for the item's base unit.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public sealed record PosLinePayload(
    PosLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null,
    string? UnitCode = null,
    string? VariantCode = null);

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
/// <param name="ParkedReceiptNo">The parked sale this was recalled from, when it was.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostReceiptRequest(
    IReadOnlyList<PosLinePayload> Lines,
    IReadOnlyList<PosTenderPayload> Tenders,
    string? CustomerNo = null,
    string? ReturnsReceiptNo = null,
    string? ParkedReceiptNo = null,
    string? OverrideReason = null);

/// <summary>What a client sends to set a sale aside.</summary>
/// <param name="Lines">What has been scanned so far.</param>
/// <param name="ParkedAs">What to call it when recalling, such as the customer's name.</param>
/// <param name="CustomerNo">Who it is for, or null for the till's walk-in customer.</param>
public sealed record ParkSaleRequest(
    IReadOnlyList<PosLinePayload> Lines,
    string? ParkedAs = null,
    string? CustomerNo = null);

/// <summary>A parked sale as it is reported back.</summary>
/// <param name="No">Its handle, which is not a receipt number because it is not a receipt yet.</param>
/// <param name="ParkedAs">What it was called when it was set aside.</param>
/// <param name="TakenAtUtc">When it was set aside.</param>
/// <param name="LineCount">How many things are in it.</param>
/// <param name="NetAmount">What it comes to, before tax.</param>
/// <param name="Lines">What is in it.</param>
public sealed record ParkedSaleView(
    string No,
    string? ParkedAs,
    DateTime TakenAtUtc,
    int LineCount,
    decimal NetAmount,
    IReadOnlyList<PosLinePayload> Lines);

/// <summary>A till, as it is reported back.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="LocationCode">Where it sells from.</param>
/// <param name="DefaultCustomerNo">Who walk-in sales are recorded against.</param>
/// <param name="IsBlocked">Whether it is out of service.</param>
/// <param name="OpenSessionNo">The session open on it, if any.</param>
/// <param name="PickBinCode">The bin this till picks from, where the location tracks bins.</param>
public sealed record PosStationView(
    string Code,
    string Name,
    string? NameArabic,
    string LocationCode,
    string DefaultCustomerNo,
    bool IsBlocked,
    string? OpenSessionNo,
    string? PickBinCode = null);

/// <summary>What a client sends to write a print template.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Content">The layout.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Kind">What it is for.</param>
/// <param name="WidthInCharacters">How wide the paper is.</param>
/// <param name="BranchId">One branch's own, or null for the company's.</param>
/// <param name="IsDefault">Whether it is used when nothing names another.</param>
/// <param name="IsActive">Whether it is still in use.</param>
public sealed record SavePrintTemplateRequest(
    string Code,
    string Name,
    string Content,
    string? NameArabic = null,
    PrintTemplateKind Kind = PrintTemplateKind.Receipt,
    int WidthInCharacters = 42,
    Guid? BranchId = null,
    bool IsDefault = false,
    bool IsActive = true);

/// <summary>What a client sends to see what an unsaved template would print.</summary>
/// <param name="Content">The layout as it stands in the editor.</param>
/// <param name="WidthInCharacters">How wide the paper is.</param>
/// <param name="ReceiptNo">A receipt to render, or null for the most recent one posted.</param>
public sealed record PreviewPrintTemplateRequest(
    string Content,
    int WidthInCharacters = 42,
    string? ReceiptNo = null);

/// <summary>One device at a till, as it is written and read back.</summary>
/// <param name="Code">Its code, which identifies it within the till.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Kind">
/// ReceiptPrinter, LabelPrinter, Scanner, CashDrawer, CustomerDisplay, Scale or PaymentTerminal.
/// </param>
/// <param name="Connection">
/// Browser, Network or Bridge. Only Bridge needs a program installed on the till, and that is the
/// distinction the whole record exists to make.
/// </param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Address">Where to find it. Required for anything but Browser.</param>
/// <param name="PrintTemplateCode">The layout it prints with, when it prints.</param>
/// <param name="IsDefault">Whether it is the one meant when a till has two of a kind.</param>
/// <param name="IsActive">Whether it may still be used.</param>
/// <param name="StationCode">The till it belongs to. Read back only.</param>
/// <param name="NeedsBridge">Whether it needs the agent. Read back only.</param>
public sealed record PosDeviceView(
    string Code,
    string Name,
    string Kind,
    string Connection,
    string? NameArabic = null,
    string? Address = null,
    string? PrintTemplateCode = null,
    bool IsDefault = false,
    bool IsActive = true,
    string? StationCode = null,
    bool NeedsBridge = false);

/// <summary>What a till needs installed on it before it can trade.</summary>
/// <param name="StationCode">The till.</param>
/// <param name="Devices">How many devices it has.</param>
/// <param name="NeedsBridge">Whether any of them needs the bridge agent.</param>
/// <param name="BridgeDevices">Which ones, so the answer can be checked rather than believed.</param>
public sealed record StationReadinessView(
    string StationCode,
    int Devices,
    bool NeedsBridge,
    IReadOnlyList<string> BridgeDevices);

/// <summary>Point of sale: tills, sessions and receipts.</summary>
public static class PosEndpoints
{
    private const string StationReadPermission = "Pos.Station.Read";

    // Devices belong to a till, so seeing and setting them up is seeing and setting up the till.
    // A separate pair would be two permissions that are always granted together.
    private const string DeviceReadPermission = StationReadPermission;
    private const string DeviceUpdatePermission = "Pos.Station.Update";
    private const string SessionReadPermission = "Pos.Session.Read";
    private const string SessionOpenPermission = "Pos.Session.Create";
    private const string SessionClosePermission = "Pos.Session.Post";
    private const string ReceiptReadPermission = "Pos.Receipt.Read";
    private const string ReceiptPostPermission = "Pos.Receipt.Post";
    private const string ReportPermission = "Pos.Report.Read";

    /// <summary>Maps the point of sale endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/pos").RequireAuthorization().WithTags("Point of sale");

        group.MapGet("/devices", DevicesAsync)
             .WithName("PosDevices")
             .WithSummary("Lists the devices at a till, or at every till.");

        group.MapGet("/stations/{stationCode}/readiness", ReadinessAsync)
             .WithName("PosStationReadiness")
             .WithSummary("Says what a till needs installed on it, which for most tills is nothing.");

        group.MapPut("/stations/{stationCode}/devices/{code}", SaveDeviceAsync)
             .WithName("SavePosDevice")
             .WithSummary("Adds a device to a till or replaces one.");

        group.MapDelete("/stations/{stationCode}/devices/{code}", RemoveDeviceAsync)
             .WithName("RemovePosDevice")
             .WithSummary("Takes a device off a till.");

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

        group.MapPost("/stations/{stationCode}/pick-bin", SetPickBinAsync)
             .WithName("SetTillPickBin")
             .WithSummary("Says which bin a till sells off, where its location tracks them.");

        group.MapGet("/sessions/{sessionNo}/reading", ReadingAsync)
             .WithName("PosXReading")
             .WithSummary("Reads a session without closing it, which is an X reading.");

        group.MapPost("/sessions/{sessionNo}/close", CloseAsync)
             .WithName("ClosePosSession")
             .WithSummary("Counts the drawer and finishes the session, which is a Z reading.");

        group.MapPost("/sessions/{sessionNo}/receipts", PostReceiptAsync)
             .WithName("PostPosReceipt")
             .WithSummary("Rings up a sale, takes the money and posts everything.");

        group.MapGet("/sessions/{sessionNo}/parked", ParkedAsync)
             .WithName("PosParkedSales")
             .WithSummary("Lists what has been set aside and not paid for at this till.");

        group.MapPost("/sessions/{sessionNo}/parked", ParkAsync)
             .WithName("ParkPosSale")
             .WithSummary("Sets a sale aside so the till can serve somebody else.");

        group.MapGet("/parked/{receiptNo}", RecallAsync)
             .WithName("RecallPosSale")
             .WithSummary("Reads a parked sale back so the till can carry on with it.");

        group.MapDelete("/parked/{receiptNo}", VoidAsync)
             .WithName("VoidPosSale")
             .WithSummary("Throws a parked sale away. Voided rather than deleted.");

        group.MapGet("/receipts/{receiptNo}/print", PrintReceiptAsync)
             .WithName("PrintReceipt")
             .WithSummary("Renders a receipt through a print template.");

        group.MapGet("/print-templates", TemplatesAsync)
             .WithName("PrintTemplates")
             .WithSummary("Lists the print templates, and the fields each kind may use.");

        group.MapPost("/print-templates", SaveTemplateAsync)
             .WithName("SavePrintTemplate")
             .WithSummary("Writes a template, or changes one that exists.");

        group.MapPost("/print-templates/preview", PreviewTemplateAsync)
             .WithName("PreviewPrintTemplate")
             .WithSummary("Renders a template that has not been saved against a real receipt.");

        group.MapGet("/reports/promotions", PromotionsReportAsync)
             .WithName("PosPromotionUptake")
             .WithSummary("What each offer moved, gave away and made, beside what the shop makes without one.");

        return app;
    }

    private static async Task<IResult> DevicesAsync(
        DeviceService devices,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? stationCode,
        CancellationToken cancellationToken)
    {
        if (!Can(user, DeviceReadPermission))
        {
            return Forbidden(DeviceReadPermission, "see till devices", http);
        }

        var found = await devices.ListAsync(stationCode, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(Render));
    }

    private static async Task<IResult> ReadinessAsync(
        string stationCode,
        DeviceService devices,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, DeviceReadPermission))
        {
            return Forbidden(DeviceReadPermission, "see till devices", http);
        }

        var result = await devices.ReadinessAsync(stationCode, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new StationReadinessView(
                result.Value.StationCode,
                result.Value.Devices,
                result.Value.NeedsBridge,
                result.Value.BridgeDevices));
    }

    private static async Task<IResult> SaveDeviceAsync(
        string stationCode,
        string code,
        PosDeviceView request,
        DeviceService devices,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, DeviceUpdatePermission))
        {
            return Forbidden(DeviceUpdatePermission, "set up till devices", http);
        }

        var result = await devices
            .SaveAsync(
                stationCode,
                new PosDevice
                {
                    Code = code,
                    Name = request.Name,
                    NameArabic = request.NameArabic,
                    Kind = Enum.TryParse<DeviceKind>(request.Kind, true, out var kind)
                        ? kind
                        : DeviceKind.ReceiptPrinter,
                    Connection = Enum.TryParse<DeviceConnection>(request.Connection, true, out var connection)
                        ? connection
                        : DeviceConnection.Browser,
                    Address = request.Address,
                    PrintTemplateCode = request.PrintTemplateCode,
                    IsDefault = request.IsDefault,
                    IsActive = request.IsActive,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                device = Render(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> RemoveDeviceAsync(
        string stationCode,
        string code,
        DeviceService devices,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, DeviceUpdatePermission))
        {
            return Forbidden(DeviceUpdatePermission, "set up till devices", http);
        }

        var result = await devices.RemoveAsync(stationCode, code, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(new { removed = code });
    }

    private static PosDeviceView Render(PosDevice device)
        => new(
            device.Code,
            device.Name,
            device.Kind.ToString(),
            device.Connection.ToString(),
            device.NameArabic,
            device.Address,
            device.PrintTemplateCode,
            device.IsDefault,
            device.IsActive,
            device.Station?.Code,
            device.NeedsBridge);

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
            openByStation.GetValueOrDefault(s.Code),
            s.PickBinCode)));
    }

    private static async Task<IResult> SetPickBinAsync(
        string stationCode,
        SetPickBinRequest request,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Pos.Station.Update"))
        {
            return Forbidden("Pos.Station.Update", "set a till's shelf", http);
        }

        var station = await context.Set<PosStation>()
            .FirstOrDefaultAsync(s => s.Code == stationCode, cancellationToken)
            .ConfigureAwait(false);

        if (station is null)
        {
            return Results.NotFound();
        }

        station.PickBinCode = string.IsNullOrWhiteSpace(request.PickBinCode)
            ? null
            : request.PickBinCode.Trim().ToUpperInvariant();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = station.Code, pickBinCode = station.PickBinCode });
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
                [.. request.Lines.Select(Line)],
                [.. request.Tenders.Select(t => new PosTenderRequest(t.Kind, t.Amount, t.Reference))],
                request.CustomerNo,
                request.ReturnsReceiptNo,
                request.ParkedReceiptNo,
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

    private static async Task<IResult> ParkedAsync(
        string sessionNo,
        PosReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReceiptReadPermission))
        {
            return Forbidden(ReceiptReadPermission, "view parked sales", http);
        }

        var parked = await receipts.ParkedAsync(sessionNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(parked.Select(View));
    }

    private static async Task<IResult> ParkAsync(
        string sessionNo,
        ParkSaleRequest request,
        PosReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ReceiptPostPermission))
        {
            return Forbidden(ReceiptPostPermission, "park a sale", http);
        }

        var result = await receipts
            .ParkAsync(
                sessionNo,
                [.. request.Lines.Select(Line)],
                request.ParkedAs,
                request.CustomerNo,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> RecallAsync(
        string receiptNo,
        PosReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReceiptReadPermission))
        {
            return Forbidden(ReceiptReadPermission, "recall a parked sale", http);
        }

        var result = await receipts.RecallAsync(receiptNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> VoidAsync(
        string receiptNo,
        PosReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReceiptPostPermission))
        {
            return Forbidden(ReceiptPostPermission, "throw a parked sale away", http);
        }

        var result = await receipts.VoidAsync(receiptNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    /// <summary>
    /// What the promotions actually did.
    /// </summary>
    /// <remarks>
    /// Behind the report permission rather than the receipt one. What a campaign cost is a
    /// question for whoever runs the shop, not for everybody who can work a till.
    /// </remarks>
    private static async Task<IResult> PromotionsReportAsync(
        PromotionUptakeReport report,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReportPermission))
        {
            return Forbidden(ReportPermission, "run till reports", http);
        }

        var last = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // A month back by default, because that is the period somebody asking this question
        // almost always means and typing two dates to find out is friction for nothing.
        var first = from ?? last.AddMonths(-1);

        var uptake = await report.RunAsync(first, last, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            from = uptake.From,
            to = uptake.To,
            totalGivenAway = uptake.TotalGivenAway,
            promotedNetRevenue = uptake.PromotedNetRevenue,
            unpromotedNetRevenue = uptake.UnpromotedNetRevenue,
            unpromotedMarginPercent = uptake.UnpromotedMarginPercent,
            offers = uptake.Offers.Select(static o => new
            {
                offerCode = o.OfferCode,
                receipts = o.Receipts,
                units = o.Units,
                givenAway = o.GivenAway,
                revenueAtList = o.RevenueAtList,
                netRevenue = o.NetRevenue,
                costOfGoods = o.CostOfGoods,
                grossProfit = o.GrossProfit,
                realisedMarginPercent = o.RealisedMarginPercent,
                discountPercent = o.DiscountPercent,
            }),
        });
    }

    private static PosLineRequest Line(PosLinePayload payload)
        => new(
            payload.Type,
            payload.No,
            payload.Quantity,
            payload.UnitPrice,
            payload.DiscountPercent,
            payload.Description,
            payload.TaxCode,
            payload.UnitCode,
            payload.VariantCode);

    private static ParkedSaleView View(PosReceipt receipt)
        => new(
            receipt.No,
            receipt.ParkedAs,
            receipt.TakenAtUtc,
            receipt.Lines.Count,
            receipt.Lines.Sum(static l => l.LineAmount),
            [.. receipt.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new PosLinePayload(
                    l.Type,
                    l.ItemNo ?? l.AccountNo ?? string.Empty,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.Description,
                    l.TaxCode))]);

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
    private static async Task<IResult> PrintReceiptAsync(
        string receiptNo,
        ReceiptPrintService printing,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? template,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Pos.Receipt.Read"))
        {
            return Forbidden("Pos.Receipt.Read", "print a receipt", http);
        }

        var result = await printing
            .ReceiptAsync(receiptNo, template, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> TemplatesAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Pos.Station.Read"))
        {
            return Forbidden("Pos.Station.Read", "see print templates", http);
        }

        var templates = await context.Set<PrintTemplate>()
            .AsNoTracking()
            .OrderBy(static t => t.Kind)
            .ThenBy(static t => t.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            templates = templates.Select(static t => new
            {
                code = t.Code,
                name = t.Name,
                nameArabic = t.NameArabic,
                kind = t.Kind.ToString(),
                content = t.Content,
                widthInCharacters = t.WidthInCharacters,
                branchId = t.BranchId,
                isDefault = t.IsDefault,
                isActive = t.IsActive,
            }),

            // Sent with the list so the editor can show what a template may refer to. A template
            // language whose fields have to be guessed at is one nobody writes against.
            fields = ReceiptPrintService.FieldsFor(PrintTemplateKind.Receipt)
                .Select(static f => new { region = f.Region, field = f.Field }),
        });
    }

    private static async Task<IResult> SaveTemplateAsync(
        SavePrintTemplateRequest request,
        AsapDbContext context,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Pos.Station.Update"))
        {
            return Forbidden("Pos.Station.Update", "change print templates", http);
        }

        var existing = await context.Set<PrintTemplate>()
            .FirstOrDefaultAsync(t => t.Code == request.Code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Set<PrintTemplate>().Add(new PrintTemplate
            {
                TenantId = tenant.TenantId ?? Guid.Empty,
                CompanyId = tenant.RequireCompanyId(),
                Code = request.Code,
                Name = request.Name,
                NameArabic = request.NameArabic,
                Kind = request.Kind,
                Content = request.Content,
                WidthInCharacters = request.WidthInCharacters,
                BranchId = request.BranchId,
                IsDefault = request.IsDefault,
                IsActive = request.IsActive,
            });
        }
        else
        {
            existing.Name = request.Name;
            existing.NameArabic = request.NameArabic;
            existing.Kind = request.Kind;
            existing.Content = request.Content;
            existing.WidthInCharacters = request.WidthInCharacters;
            existing.BranchId = request.BranchId;
            existing.IsDefault = request.IsDefault;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = request.Code, created = existing is null });
    }

    /// <summary>
    /// Renders an unsaved template against a real receipt.
    /// </summary>
    /// <remarks>
    /// Against a real one rather than an invented one. A layout that looks right beside made-up
    /// figures is how a receipt ships with a total column too narrow for four digits.
    /// </remarks>
    private static async Task<IResult> PreviewTemplateAsync(
        PreviewPrintTemplateRequest request,
        AsapDbContext context,
        ReceiptPrintService printing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Pos.Station.Read"))
        {
            return Forbidden("Pos.Station.Read", "preview a print template", http);
        }

        var receiptNo = request.ReceiptNo;

        if (string.IsNullOrWhiteSpace(receiptNo))
        {
            receiptNo = await context.Set<PosReceipt>()
                .AsNoTracking()
                .Where(static r => r.Status == PosReceiptStatus.Posted)
                .OrderByDescending(static r => r.TakenAtUtc)
                .Select(static r => r.No)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(receiptNo))
        {
            return Results.Ok(new
            {
                text = string.Empty,
                widthInCharacters = request.WidthInCharacters,
                receiptNo = (string?)null,
            });
        }

        // Saved into nothing: the content on the wire is rendered directly, so somebody can see
        // what an edit does before committing the shop to it.
        var scratch = new PrintTemplate
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            Code = "PREVIEW",
            Name = "Preview",
            Content = request.Content,
            WidthInCharacters = request.WidthInCharacters,
        };

        var rendered = await printing
            .PreviewAsync(receiptNo, scratch, cancellationToken)
            .ConfigureAwait(false);

        return rendered.Failed
            ? Refused(rendered, http)
            : Results.Ok(new
            {
                text = rendered.Value.Text,
                widthInCharacters = rendered.Value.WidthInCharacters,
                receiptNo,
            });
    }

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
