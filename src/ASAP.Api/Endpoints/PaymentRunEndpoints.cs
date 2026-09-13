using System.Text;
using ASAP.Api.Infrastructure;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Payments;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>One invoice a transfer settles.</summary>
/// <param name="DocumentNo">The invoice.</param>
/// <param name="DueDate">When it fell due.</param>
/// <param name="Amount">How much of it is paid.</param>
public sealed record PaymentRunInvoiceView(string? DocumentNo, DateOnly? DueDate, decimal Amount);

/// <summary>One transfer to one vendor.</summary>
/// <param name="LineNo">Position in the run.</param>
/// <param name="VendorNo">Who is paid.</param>
/// <param name="VendorName">Their name.</param>
/// <param name="Iban">Where the money goes, as copied at proposal.</param>
/// <param name="IbanValid">Whether a bank would accept it.</param>
/// <param name="Reference">What reaches the vendor's statement.</param>
/// <param name="Amount">What the transfer pays.</param>
/// <param name="Invoices">What it settles.</param>
public sealed record PaymentRunLineView(
    int LineNo,
    string VendorNo,
    string VendorName,
    string? Iban,
    bool IbanValid,
    string Reference,
    decimal Amount,
    IReadOnlyList<PaymentRunInvoiceView> Invoices);

/// <summary>A payment run, without its file.</summary>
/// <param name="No">The run number.</param>
/// <param name="BankAccountCode">Where the money leaves.</param>
/// <param name="PaymentDate">When the bank should pay.</param>
/// <param name="DueByDate">What was proposed.</param>
/// <param name="CurrencyCode">What it pays in.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="TotalAmount">What it pays in total.</param>
/// <param name="CreatedByUserName">Who proposed it.</param>
/// <param name="ExportedByUserName">Who made the file.</param>
/// <param name="ExportedAtUtc">When.</param>
/// <param name="FileSha256">The file's fingerprint.</param>
/// <param name="TransactionNo">The posting, once posted.</param>
/// <param name="Lines">The transfers.</param>
public sealed record PaymentRunView(
    string No,
    string BankAccountCode,
    DateOnly PaymentDate,
    DateOnly DueByDate,
    string CurrencyCode,
    PaymentRunStatus Status,
    decimal TotalAmount,
    string? CreatedByUserName,
    string? ExportedByUserName,
    DateTime? ExportedAtUtc,
    string? FileSha256,
    long? TransactionNo,
    IReadOnlyList<PaymentRunLineView> Lines);

/// <summary>What a client sends to propose a run.</summary>
/// <param name="BankAccountCode">Where the money leaves.</param>
/// <param name="PaymentDate">When the bank should pay.</param>
/// <param name="DueByDate">Invoices due on or before this are proposed.</param>
public sealed record ProposePaymentRunRequest(string BankAccountCode, DateOnly PaymentDate, DateOnly DueByDate);

/// <summary>What a client sends to set a vendor's bank details.</summary>
/// <param name="Iban">The IBAN, or null to clear it.</param>
/// <param name="Bic">Their bank's BIC.</param>
/// <param name="BankName">Their bank's name.</param>
public sealed record VendorBankDetailsRequest(string? Iban, string? Bic, string? BankName);

/// <summary>Vendor payment runs and the bank files they make.</summary>
public static class PaymentRunEndpoints
{
    private const string ReadPermission = "Finance.Payment.Read";
    private const string ProposePermission = "Finance.Payment.Create";
    private const string ReleasePermission = "Finance.Payment.Post";
    private const string VendorPermission = "Finance.Party.Update";

    /// <summary>Maps the payment run endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPaymentRunEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/finance").RequireAuthorization().WithTags("Finance");

        group.MapGet("/payment-runs", ListAsync)
             .WithName("PaymentRuns")
             .WithSummary("Payment runs, most recent first.");

        group.MapGet("/payment-runs/{runNo}", GetAsync)
             .WithName("PaymentRun")
             .WithSummary("One run, its transfers and the invoices each settles.");

        group.MapPost("/payment-runs", ProposeAsync)
             .WithName("ProposePaymentRun")
             .WithSummary("Proposes paying everything due by a date. Nothing leaves the bank.");

        group.MapDelete("/payment-runs/{runNo}/lines/{lineNo:int}", RemoveLineAsync)
             .WithName("RemovePaymentRunLine")
             .WithSummary("Takes one vendor off a draft run.");

        group.MapPost("/payment-runs/{runNo}/export", ExportAsync)
             .WithName("ExportPaymentRun")
             .WithSummary("Makes the pain.001 file and freezes the run.");

        group.MapGet("/payment-runs/{runNo}/file", FileAsync)
             .WithName("PaymentRunFile")
             .WithSummary("The file exactly as it was made.");

        group.MapPost("/payment-runs/{runNo}/post", PostAsync)
             .WithName("PostPaymentRun")
             .WithSummary("Records what was paid and settles the invoices.");

        group.MapPost("/payment-runs/{runNo}/cancel", CancelAsync)
             .WithName("CancelPaymentRun")
             .WithSummary("Abandons a run that has not posted.");

        group.MapPut("/vendors/{vendorNo}/bank", SetBankDetailsAsync)
             .WithName("SetVendorBankDetails")
             .WithSummary("Records where a vendor is paid. Every change is logged.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view payment runs", http);
        }

        var rows = await runs.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(View));
    }

    private static async Task<IResult> GetAsync(
        string runNo,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view payment runs", http);
        }

        var run = await runs.LoadAsync(runNo, cancellationToken).ConfigureAwait(false);

        return run is null ? Results.NotFound() : Results.Ok(View(run));
    }

    private static async Task<IResult> ProposeAsync(
        ProposePaymentRunRequest request,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ProposePermission))
        {
            return Forbidden(ProposePermission, "propose a payment run", http);
        }

        var result = await runs
            .ProposeAsync(request.BankAccountCode, request.PaymentDate, request.DueByDate, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result, http);
    }

    private static async Task<IResult> RemoveLineAsync(
        string runNo,
        int lineNo,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ProposePermission))
        {
            return Forbidden(ProposePermission, "change a payment run", http);
        }

        return Respond(await runs.RemoveLineAsync(runNo, lineNo, cancellationToken).ConfigureAwait(false), http);
    }

    private static async Task<IResult> ExportAsync(
        string runNo,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReleasePermission))
        {
            return Forbidden(ReleasePermission, "release a payment run", http);
        }

        var result = await runs.ExportAsync(runNo, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                fileName = result.Value.FileName,
                sha256 = result.Value.Sha256,
                transferCount = result.Value.TransferCount,
                controlSum = result.Value.ControlSum,
            });
    }

    private static async Task<IResult> FileAsync(
        string runNo,
        PaymentRunService runs,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // Reading the file is releasing it: whoever holds it can hand it to the bank.
        if (!Can(user, ReleasePermission))
        {
            return Forbidden(ReleasePermission, "download a payment file", http);
        }

        var file = await context.Set<PaymentRun>()
            .AsNoTracking()
            .Where(r => r.No == runNo)
            .Select(static r => new { r.No, r.FileContent })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (file?.FileContent is null)
        {
            return Results.NotFound();
        }

        // Served exactly as stored, never regenerated. A second download must be the same bytes the
        // hash was taken of, or the hash proves nothing.
        return Results.File(Encoding.UTF8.GetBytes(file.FileContent), "application/xml", $"{file.No}.xml");
    }

    private static async Task<IResult> PostAsync(
        string runNo,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReleasePermission))
        {
            return Forbidden(ReleasePermission, "post a payment run", http);
        }

        return Respond(await runs.PostAsync(runNo, cancellationToken).ConfigureAwait(false), http);
    }

    private static async Task<IResult> CancelAsync(
        string runNo,
        PaymentRunService runs,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ProposePermission))
        {
            return Forbidden(ProposePermission, "cancel a payment run", http);
        }

        return Respond(await runs.CancelAsync(runNo, cancellationToken).ConfigureAwait(false), http);
    }

    private static async Task<IResult> SetBankDetailsAsync(
        string vendorNo,
        VendorBankDetailsRequest request,
        VendorBankDetailsService bank,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, VendorPermission))
        {
            return Forbidden(VendorPermission, "change a vendor's bank details", http);
        }

        var result = await bank
            .SetAsync(vendorNo, request.Iban, request.Bic, request.BankName, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                vendorNo = result.Value.No,
                iban = result.Value.Iban,
                bic = result.Value.Bic,
                bankName = result.Value.BankName,
            });
    }

    private static IResult Respond(Result<PaymentRun> result, HttpContext http)
        => result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                run = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });

    private static PaymentRunView View(PaymentRun run)
        => new(
            run.No,
            run.BankAccountCode,
            run.PaymentDate,
            run.DueByDate,
            run.CurrencyCode,
            run.Status,
            run.TotalAmount,
            run.CreatedByUserName,
            run.ExportedByUserName,
            run.ExportedAtUtc,
            run.FileSha256,
            run.TransactionNo,
            [.. run.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new PaymentRunLineView(
                    l.LineNo,
                    l.VendorNo,
                    l.VendorName,
                    l.Iban,
                    Iban.IsValid(l.Iban),
                    l.Reference,
                    l.Amount,
                    [.. l.Invoices
                        .OrderBy(static i => i.DueDate)
                        .Select(static i => new PaymentRunInvoiceView(i.DocumentNo, i.DueDate, i.Amount))]))]);

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
