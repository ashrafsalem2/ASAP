using ASAP.Api.Infrastructure;
using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Posting;
using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>An account as the client sees it.</summary>
/// <param name="Id">The account key.</param>
/// <param name="No">The account number.</param>
/// <param name="Name">The account name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="AccountType">Whether it takes entries or shapes the report.</param>
/// <param name="Category">Which statement it belongs to.</param>
/// <param name="Indentation">Indent level on the printed chart.</param>
/// <param name="AllowsDirectPosting">Whether a person may post to it by hand.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
/// <param name="Balance">Running balance in company currency.</param>
public sealed record GlAccountSummary(
    Guid Id,
    string No,
    string Name,
    string? NameArabic,
    string AccountType,
    string Category,
    int Indentation,
    bool AllowsDirectPosting,
    bool IsBlocked,
    decimal Balance);

/// <summary>A posted ledger entry as the client sees it.</summary>
/// <param name="Id">The entry key.</param>
/// <param name="PostingDate">The date it is reported in.</param>
/// <param name="TransactionNo">The transaction it belongs to.</param>
/// <param name="AccountNo">The account it landed on.</param>
/// <param name="Description">What it says.</param>
/// <param name="DebitAmount">The debit side.</param>
/// <param name="CreditAmount">The credit side.</param>
/// <param name="DocumentNo">The document it came from.</param>
/// <param name="SourceCode">Which part of the system wrote it.</param>
public sealed record GlEntrySummary(
    Guid Id,
    DateOnly PostingDate,
    long TransactionNo,
    string AccountNo,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    string? DocumentNo,
    string SourceCode);

/// <summary>Chart of accounts, journals and the general ledger.</summary>
public static class FinanceEndpoints
{
    /// <summary>Maps the Finance endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/finance").RequireAuthorization().WithTags("Finance");

        group.MapGet("/accounts", AccountsAsync)
             .WithName("ChartOfAccounts")
             .WithSummary("Lists the chart of accounts for the active company.");

        group.MapPost("/journals/post", PostJournalAsync)
             .WithName("PostJournal")
             .WithSummary("Posts journal lines to the general ledger.");

        group.MapGet("/entries", EntriesAsync)
             .WithName("LedgerEntries")
             .WithSummary("Lists posted general ledger entries, most recent first.");

        group.MapPost("/journals/reverse", ReverseAsync)
             .WithName("ReverseTransaction")
             .WithSummary("Reverses a posted transaction by posting its mirror image.");

        group.MapGet("/reports/trial-balance", TrialBalanceAsync)
             .WithName("TrialBalance")
             .WithSummary("Reports opening balance, movement and closing balance per account.");

        return app;
    }

    private static async Task<IResult> AccountsAsync(
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var accounts = await context.Set<GlAccount>()
            .AsNoTracking()
            .OrderBy(a => a.No)
            .Select(a => new GlAccountSummary(
                a.Id,
                a.No,
                a.Name,
                a.NameArabic,
                a.AccountType.ToString(),
                a.Category.ToString(),
                a.Indentation,
                a.AllowsDirectPosting,
                a.IsBlocked,
                a.Balance))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(accounts);
    }

    private static async Task<IResult> PostJournalAsync(
        PostJournalCommand command,
        IDispatcher dispatcher,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            // Every reason the posting was refused travels back at once, each with its resolution
            // and the field it belongs to, so the client can mark up the offending lines rather
            // than showing one message and hiding the rest.
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        var receipt = result.Value;

        return Results.Ok(new
        {
            transactionNo = receipt.TransactionNo,
            documentNo = receipt.DocumentNo,
            entryCount = receipt.EntryCount,
            totalAmount = receipt.TotalAmount,

            // Warnings ride along with the success. A posting that went through on an override
            // should say so on the screen, not only in the audit log.
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static async Task<IResult> EntriesAsync(
        AsapDbContext context,
        [FromQuery] string? accountNo,
        [FromQuery] long? transactionNo,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var query = context.Set<GlEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(accountNo))
        {
            query = query.Where(e => e.AccountNo == accountNo);
        }

        if (transactionNo is { } transaction)
        {
            query = query.Where(e => e.TransactionNo == transaction);
        }

        var entries = await query
            .OrderByDescending(e => e.TransactionNo)
            .ThenBy(e => e.AccountNo)

            // Capped, and capped here rather than trusting the caller. A ledger grows to millions
            // of rows, and an unbounded list endpoint is a way to take the server down by accident.
            .Take(Math.Clamp(take ?? 100, 1, 500))
            .Select(e => new GlEntrySummary(
                e.Id,
                e.PostingDate,
                e.TransactionNo,
                e.AccountNo,
                e.Description,
                e.DebitAmount,
                e.CreditAmount,
                e.DocumentNo,
                e.SourceCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(entries);
    }

    private static async Task<IResult> ReverseAsync(
        ReverseTransactionCommand command,
        IDispatcher dispatcher,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        return Results.Ok(new
        {
            reversedTransactionNo = command.TransactionNo,
            transactionNo = result.Value.TransactionNo,
            entryCount = result.Value.EntryCount,
            totalAmount = result.Value.TotalAmount,
        });
    }

    private static async Task<IResult> TrialBalanceAsync(
        IDispatcher dispatcher,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool? includeAll,
        IClock clock,
        CancellationToken cancellationToken)
    {
        // Defaults to the current year, which is what someone opening the report almost always
        // wants, and saves them constructing a date range to see anything at all.
        var today = clock.Today;

        var query = new TrialBalanceQuery(
            from ?? new DateOnly(today.Year, 1, 1),
            to ?? today,
            includeAll ?? false);

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }
}
