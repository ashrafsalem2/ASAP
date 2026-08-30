using ASAP.Api.Infrastructure;
using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Posting;
using ASAP.Modules.Finance.Reporting;
using ASAP.Modules.Finance.Currencies;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to close a financial year.</summary>
/// <param name="LockTheYear">Whether to stop the year accepting further postings once it is done.</param>
/// <param name="Reason">What the entries should say beyond that they are the year-end transfer.</param>
public sealed record CloseYearRequest(bool LockTheYear = true, string? Reason = null);

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

/// <summary>One line of a recurring batch, as it is written and read back.</summary>
/// <param name="AccountNo">The account it posts to.</param>
/// <param name="Description">What the entry says.</param>
/// <param name="RecurrenceFormula">
/// How far each posting moves the next one: <c>1M</c> a month on, <c>1M+CM</c> the last day of
/// next month, <c>3M</c> a quarter.
/// </param>
/// <param name="Amount">The amount. Ignored for a Balance line, which posts what it finds.</param>
/// <param name="Method">Fixed, Variable, Balance, ReversingFixed or ReversingVariable.</param>
/// <param name="BalancingAccountNo">Where the other side goes, when the line balances itself.</param>
/// <param name="NextPostingDate">The next day it is due, or null when it has finished.</param>
/// <param name="ExpiresOn">The day after which it stops, or null to run forever.</param>
/// <param name="Dimensions">How it is analysed, as <c>CODE=VALUE</c> pairs.</param>
public sealed record RecurringLineRequest(
    string AccountNo,
    string Description,
    string RecurrenceFormula,
    decimal Amount = 0m,
    string Method = "Fixed",
    string? BalancingAccountNo = null,
    DateOnly? NextPostingDate = null,
    DateOnly? ExpiresOn = null,
    string? Dimensions = null);

/// <summary>What a client sends to create or rewrite a recurring batch.</summary>
/// <param name="Code">The short code, which identifies it.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Lines">The lines.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Description">What it is for.</param>
/// <param name="IsActive">Whether it may still be posted.</param>
public sealed record SaveRecurringRequest(
    string Code,
    string Name,
    IReadOnlyList<RecurringLineRequest> Lines,
    string? NameArabic = null,
    string? Description = null,
    bool IsActive = true);

/// <summary>One row of a statement layout, as it is written and read back.</summary>
/// <param name="RowNo">What formulas call it, for example <c>R100</c>.</param>
/// <param name="Description">What it is called on the page.</param>
/// <param name="Kind">Accounts, Formula or Heading.</param>
/// <param name="Expression">The account range, or the formula.</param>
/// <param name="DescriptionArabic">What it is called in Arabic.</param>
/// <param name="AmountKind">NetChange for a movement, BalanceAtDate for a balance.</param>
/// <param name="ShowOppositeSign">
/// Whether to turn the sign. Applied before formulas run, so a formula means what it looks like.
/// </param>
/// <param name="Indent">How far to indent the description.</param>
/// <param name="IsBold">Whether it is a total.</param>
/// <param name="HideIfZero">Whether to leave it out when its figure is nought.</param>
public sealed record ScheduleLineRequest(
    string RowNo,
    string Description,
    string Kind = "Accounts",
    string? Expression = null,
    string? DescriptionArabic = null,
    string AmountKind = "NetChange",
    bool ShowOppositeSign = false,
    int Indent = 0,
    bool IsBold = false,
    bool HideIfZero = false);

/// <summary>What a client sends to create or rewrite a statement layout.</summary>
/// <remarks>
/// The whole layout, rows included. A layout is read, edited and sent back as one thing, because
/// a row only means anything alongside the rows its formulas name — sending one row at a time
/// would allow a save that leaves a formula pointing at a row that no longer exists.
/// </remarks>
/// <param name="Code">The short code, which identifies it.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Lines">The rows, in the order they print.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Description">What it is for.</param>
/// <param name="IsActive">Whether it may still be run.</param>
public sealed record SaveScheduleRequest(
    string Code,
    string Name,
    IReadOnlyList<ScheduleLineRequest> Lines,
    string? NameArabic = null,
    string? Description = null,
    bool IsActive = true);

/// <summary>A currency and what it is worth today, as it is reported back.</summary>
/// <param name="Code">The ISO code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Symbol">The symbol shown beside an amount.</param>
/// <param name="DecimalPlaces">How many places amounts in it are rounded to.</param>
/// <param name="IsActive">Whether it may still be used on a new document.</param>
/// <param name="Rate">
/// What one unit is worth today, or null when today has no rate. For showing on a screen only —
/// a posting resolves the rate from its own document date, never from this.
/// </param>
/// <param name="RateStartingOn">The day today's rate came into force.</param>
public sealed record CurrencyView(
    string Code,
    string Name,
    string? NameArabic,
    string? Symbol,
    int DecimalPlaces,
    bool IsActive,
    decimal? Rate,
    DateOnly? RateStartingOn);

/// <summary>One dated rate, as it is reported back.</summary>
/// <param name="StartingDate">The first day it applies to.</param>
/// <param name="CurrencyAmount">How many units the pair is quoted for.</param>
/// <param name="BaseAmount">What those units are worth in company currency.</param>
/// <param name="Multiplier">The two divided, for reading.</param>
public sealed record ExchangeRateView(
    DateOnly StartingDate,
    decimal CurrencyAmount,
    decimal BaseAmount,
    decimal Multiplier);

/// <summary>What a client sends to add or change a currency.</summary>
/// <remarks>
/// The whole record, not a patch. Leaving a field out sets it to its default, which is the same
/// bargain every other upsert in ASAP makes — read the currency, change what you mean, send it
/// back.
/// </remarks>
/// <param name="Code">The ISO code, which identifies it.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Symbol">The symbol shown beside an amount.</param>
/// <param name="DecimalPlaces">How many places amounts in it are rounded to.</param>
/// <param name="IsActive">Whether it may still be used on a new document.</param>
public sealed record SaveCurrencyRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? Symbol = null,
    int DecimalPlaces = 2,
    bool IsActive = true);

/// <summary>What a client sends to enter a rate.</summary>
/// <param name="StartingDate">The first day it applies to.</param>
/// <param name="BaseAmount">What the quoted units are worth in company currency.</param>
/// <param name="CurrencyAmount">
/// How many units the pair is quoted for, usually one. Use a hundred for a currency worth a small
/// fraction of the company's own, so the rate is stated exactly rather than rounded.
/// </param>
public sealed record SaveExchangeRateRequest(
    DateOnly StartingDate,
    decimal BaseAmount,
    decimal CurrencyAmount = 1m);

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

        group.MapGet("/reports/income-statement", IncomeStatementAsync)
             .WithName("IncomeStatement")
             .WithSummary("Reports revenue, cost of sales and expenses over a range.");

        group.MapGet("/fiscal-years", FiscalYearsAsync)
             .WithName("FiscalYears")
             .WithSummary("Lists the financial years, and whether each has been closed.");

        group.MapPost("/fiscal-years/{yearCode}/close", CloseFiscalYearAsync)
             .WithName("CloseFiscalYear")
             .WithSummary("Transfers a year's result to retained earnings and locks the year.");

        group.MapGet("/reports/branch-performance", BranchPerformanceAsync)
             .WithName("BranchPerformance")
             .WithSummary("Reports what each branch earned and spent, and what was charged to none.");

        group.MapGet("/reports/balance-sheet", BalanceSheetAsync)
             .WithName("BalanceSheet")
             .WithSummary("Reports what the company owned and owed on a given day.");

        group.MapGet("/reports/tax-return", TaxReturnAsync)
             .WithName("TaxReturn")
             .WithSummary("Reports tax charged and tax paid for a period, and the net owed.");

        group.MapGet("/recurring", RecurringAsync)
             .WithName("RecurringJournals")
             .WithSummary("Lists the recurring batches and when each next falls due.");

        group.MapPut("/recurring/{code}", SaveRecurringAsync)
             .WithName("SaveRecurringJournal")
             .WithSummary("Creates a recurring batch or rewrites one, lines and all.");

        group.MapPost("/recurring/{code}/post", PostRecurringAsync)
             .WithName("PostRecurringJournal")
             .WithSummary("Posts every line of a batch that is due, and moves those lines on.");

        group.MapGet("/schedules", SchedulesAsync)
             .WithName("AccountSchedules")
             .WithSummary("Lists the statement layouts this company can run.");

        group.MapGet("/schedules/{code}/layout", ScheduleLayoutAsync)
             .WithName("AccountScheduleLayout")
             .WithSummary("Reads one layout's rows, for editing.");

        group.MapPut("/schedules/{code}", SaveScheduleAsync)
             .WithName("SaveAccountSchedule")
             .WithSummary("Creates a layout or rewrites one, rows and all.");

        group.MapGet("/schedules/{code}", RunScheduleAsync)
             .WithName("RunAccountSchedule")
             .WithSummary("Runs one statement layout over a period.");

        group.MapGet("/currencies", CurrenciesAsync)
             .WithName("Currencies")
             .WithSummary("Lists the currencies the company transacts in, and what each is worth today.");

        group.MapPut("/currencies/{code}", SaveCurrencyAsync)
             .WithName("SaveCurrency")
             .WithSummary("Adds a currency or replaces one, whole.");

        group.MapGet("/currencies/{code}/rates", RatesAsync)
             .WithName("ExchangeRates")
             .WithSummary("Lists a currency's rates, most recent first.");

        group.MapPut("/currencies/{code}/rates", SaveRateAsync)
             .WithName("SaveExchangeRate")
             .WithSummary("Enters the rate from a date, replacing any rate already starting on it.");

        group.MapGet("/tax-codes", TaxCodesAsync)
             .WithName("TaxCodes")
             .WithSummary("Lists the tax codes a document can carry, with the rate in force today.");

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

    private static async Task<IResult> IncomeStatementAsync(
        IDispatcher dispatcher,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool? comparePreviousYear,
        [FromQuery] bool? includeAll,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var start = from ?? new DateOnly(today.Year, 1, 1);
        var end = to ?? today;

        // The same span a year earlier, which is the comparison an income statement is almost
        // always read against. Offered rather than assumed: a company in its first year has
        // nothing to compare with, and an empty column invites the wrong conclusion.
        var compare = comparePreviousYear == true;

        var query = new IncomeStatementQuery(
            start,
            end,
            compare ? start.AddYears(-1) : null,
            compare ? end.AddYears(-1) : null,
            includeAll ?? false);

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> FiscalYearsAsync(
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var years = await context.Set<FiscalYear>()
            .AsNoTracking()
            .OrderByDescending(static y => y.StartDate)
            .Select(static y => new
            {
                code = y.Code,
                startDate = y.StartDate,
                endDate = y.EndDate,

                // Two different things, and conflating them is how a year gets locked with its
                // result still inside it. Locked stops posting; transferred is the entry that
                // moves the result out.
                isClosed = y.IsClosed,
                incomeTransferred = y.IncomeTransferred,
                closedAtUtc = y.ClosedAtUtc,
                periods = y.Periods
                    .OrderBy(p => p.StartDate)
                    .Select(p => new { p.Name, p.StartDate, p.EndDate, p.IsClosed }),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(years);
    }

    private static async Task<IResult> CloseFiscalYearAsync(
        string yearCode,
        CloseYearRequest? request,
        IDispatcher dispatcher,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var command = new CloseFiscalYearCommand(
            yearCode,
            request?.LockTheYear ?? true,
            request?.Reason);

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        return Results.Ok(new
        {
            receipt = result.Value,
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static async Task<IResult> BranchPerformanceAsync(
        IDispatcher dispatcher,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool? includeInactive,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;

        // The month to date by default, not the year. A branch report is read to decide something
        // about this month; a year-to-date figure buries a shop that has been losing money since
        // April under the three good months before it.
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        var query = new BranchPerformanceQuery(start, end, includeInactive ?? false);

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> TaxReturnAsync(
        IDispatcher dispatcher,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool? includeFiled,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;

        // Defaults to the current quarter, which is the period most returns cover and saves the
        // reader working out where it started.
        var quarterStart = new DateOnly(today.Year, (((today.Month - 1) / 3) * 3) + 1, 1);

        var query = new TaxReturnQuery(
            from ?? quarterStart,
            to ?? today,
            includeFiled ?? false);

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> RecurringAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, RecurringReadPermission))
        {
            return Forbidden(RecurringReadPermission, "see recurring journals", http);
        }

        var batches = await context.Set<RecurringJournalBatch>()
            .AsNoTracking()
            .Include(b => b.Lines)
            .OrderBy(b => b.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(batches.Select(static b => new
        {
            code = b.Code,
            name = b.Name,
            nameArabic = b.NameArabic,
            description = b.Description,
            isActive = b.IsActive,
            nextDue = b.NextDue,
            lines = b.Lines
                .OrderBy(static l => l.Order)
                .Select(static l => new RecurringLineRequest(
                    l.AccountNo,
                    l.Description,
                    l.RecurrenceFormula,
                    l.Amount,
                    l.Method.ToString(),
                    l.BalancingAccountNo,
                    l.NextPostingDate,
                    l.ExpiresOn,
                    l.Dimensions)),
        }));
    }

    private static async Task<IResult> SaveRecurringAsync(
        string code,
        SaveRecurringRequest request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, RecurringUpdatePermission))
        {
            return Forbidden(RecurringUpdatePermission, "maintain recurring journals", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        // Through the execution strategy, because the connection retries on transient faults and
        // will not allow a hand-rolled transaction otherwise.
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy
            .ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                var batch = await context.Set<RecurringJournalBatch>()
                    .Include(b => b.Lines)
                    .FirstOrDefaultAsync(b => b.Code == normalised, cancellationToken)
                    .ConfigureAwait(false);

                if (batch is null)
                {
                    batch = new RecurringJournalBatch
                    {
                        TenantId = tenantContext.TenantId ?? Guid.Empty,
                        CompanyId = tenantContext.RequireCompanyId(),
                        Code = normalised,
                        Name = request.Name,
                    };

                    context.Set<RecurringJournalBatch>().Add(batch);
                }

                batch.Name = request.Name;
                batch.NameArabic = request.NameArabic;
                batch.Description = request.Description;
                batch.IsActive = request.IsActive;

                context.Set<RecurringJournalLine>().RemoveRange(batch.Lines);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var order = 0;

                foreach (var line in request.Lines)
                {
                    context.Set<RecurringJournalLine>().Add(new RecurringJournalLine
                    {
                        TenantId = batch.TenantId,
                        CompanyId = batch.CompanyId,
                        RecurringJournalBatchId = batch.Id,
                        Order = ++order,
                        AccountNo = line.AccountNo,
                        BalancingAccountNo = line.BalancingAccountNo,
                        Description = line.Description,
                        Amount = line.Amount,
                        Method = Enum.TryParse<RecurringMethod>(line.Method, true, out var method)
                            ? method
                            : RecurringMethod.Fixed,
                        RecurrenceFormula = line.RecurrenceFormula,
                        NextPostingDate = line.NextPostingDate,
                        ExpiresOn = line.ExpiresOn,
                        Dimensions = line.Dimensions,
                    });
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return Results.Ok(new { code = normalised, lines = request.Lines.Count });
    }

    private static async Task<IResult> PostRecurringAsync(
        string code,
        RecurringJournalService recurring,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        if (!Can(user, RecurringPostPermission))
        {
            return Forbidden(RecurringPostPermission, "post a recurring journal", http);
        }

        var result = await recurring.PostAsync(code, on, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages))
            : Results.Ok(new
            {
                run = result.Value,
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> SchedulesAsync(
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var schedules = await context.Set<AccountSchedule>()
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new
            {
                code = s.Code,
                name = s.Name,
                nameArabic = s.NameArabic,
                description = s.Description,
                rows = s.Lines.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(schedules);
    }

    private static async Task<IResult> ScheduleLayoutAsync(
        string code,
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var normalised = code.Trim().ToUpperInvariant();

        var schedule = await context.Set<AccountSchedule>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (schedule is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            code = schedule.Code,
            name = schedule.Name,
            nameArabic = schedule.NameArabic,
            description = schedule.Description,
            isActive = schedule.IsActive,
            lines = schedule.Lines
                .OrderBy(static l => l.Order)
                .Select(static l => new ScheduleLineRequest(
                    l.RowNo,
                    l.Description,
                    l.Kind.ToString(),
                    l.Expression,
                    l.DescriptionArabic,
                    l.AmountKind.ToString(),
                    l.ShowOppositeSign,
                    l.Indent,
                    l.IsBold,
                    l.HideIfZero)),
        });
    }

    private static async Task<IResult> SaveScheduleAsync(
        string code,
        SaveScheduleRequest request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ScheduleUpdatePermission))
        {
            return Forbidden(ScheduleUpdatePermission, "edit statement layouts", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        // Through the execution strategy, because the connection retries on transient faults and
        // will not allow a hand-rolled transaction otherwise. Everything the save touches is read
        // inside the delegate, so a retry starts from the database rather than from a change
        // tracker the failed attempt already emptied.
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy
            .ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                var schedule = await context.Set<AccountSchedule>()
                    .Include(s => s.Lines)
                    .FirstOrDefaultAsync(s => s.Code == normalised, cancellationToken)
                    .ConfigureAwait(false);

                if (schedule is null)
                {
                    schedule = new AccountSchedule
                    {
                        TenantId = tenantContext.TenantId ?? Guid.Empty,
                        CompanyId = tenantContext.RequireCompanyId(),
                        Code = normalised,
                        Name = request.Name,
                    };

                    context.Set<AccountSchedule>().Add(schedule);
                }

                schedule.Name = request.Name;
                schedule.NameArabic = request.NameArabic;
                schedule.Description = request.Description;
                schedule.IsActive = request.IsActive;

                // Replaced wholesale rather than merged. A row is only meaningful beside the rows
                // its formulas name, so the set is saved as a set -- and matching old rows to new
                // ones by name would quietly keep a row somebody deleted.
                //
                // In two saves, and that is not fussiness. Row names are unique per layout, so a
                // single save would offer the database the new R10 before it had taken the old one
                // away, and the index would refuse it. Renaming or reordering a row -- the
                // commonest edit there is -- would fail every time.
                context.Set<AccountScheduleLine>().RemoveRange(schedule.Lines);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var order = 0;

                foreach (var line in request.Lines)
                {
                    context.Set<AccountScheduleLine>().Add(new AccountScheduleLine
                    {
                        TenantId = schedule.TenantId,
                        CompanyId = schedule.CompanyId,
                        AccountScheduleId = schedule.Id,
                        Order = ++order,
                        RowNo = line.RowNo,
                        Description = line.Description,
                        DescriptionArabic = line.DescriptionArabic,
                        Kind = Enum.TryParse<ScheduleRowKind>(line.Kind, true, out var kind)
                            ? kind
                            : ScheduleRowKind.Accounts,
                        AmountKind = Enum.TryParse<ScheduleAmountKind>(line.AmountKind, true, out var amountKind)
                            ? amountKind
                            : ScheduleAmountKind.NetChange,
                        Expression = line.Expression,
                        ShowOppositeSign = line.ShowOppositeSign,
                        Indent = line.Indent,
                        IsBold = line.IsBold,
                        HideIfZero = line.HideIfZero,
                    });
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return Results.Ok(new { code = normalised, rows = request.Lines.Count });
    }


    private static async Task<IResult> RunScheduleAsync(
        string code,
        IDispatcher dispatcher,
        HttpContext http,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new AccountScheduleQuery(code, from, to, branchId), cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages))
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> CurrenciesAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        IClock clock,
        [FromQuery] bool? includeInactive,
        CancellationToken cancellationToken)
    {
        if (!Can(user, CurrencyReadPermission))
        {
            return Forbidden(CurrencyReadPermission, "see currencies and rates", http);
        }

        var query = context.Set<Currency>().AsNoTracking().Include(c => c.Rates);

        var currencies = await (includeInactive == true ? query : query.Where(c => c.IsActive))
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = clock.Today;

        return Results.Ok(currencies.Select(c =>
        {
            var rate = c.RateOn(today);

            return new CurrencyView(
                c.Code,
                c.Name,
                c.NameArabic,
                c.Symbol,
                c.DecimalPlaces,
                c.IsActive,
                rate?.Multiplier,
                rate?.StartingDate);
        }));
    }

    private static async Task<IResult> SaveCurrencyAsync(
        string code,
        SaveCurrencyRequest request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CurrencyUpdatePermission))
        {
            return Forbidden(CurrencyUpdatePermission, "maintain currencies", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var currency = await context.Set<Currency>()
            .FirstOrDefaultAsync(c => c.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (currency is null)
        {
            currency = new Currency
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.RequireCompanyId(),
                Code = normalised,
                Name = request.Name,
            };

            context.Set<Currency>().Add(currency);
        }

        currency.Name = request.Name;
        currency.NameArabic = request.NameArabic;
        currency.Symbol = request.Symbol;
        currency.DecimalPlaces = request.DecimalPlaces;
        currency.IsActive = request.IsActive;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new CurrencyView(
            currency.Code,
            currency.Name,
            currency.NameArabic,
            currency.Symbol,
            currency.DecimalPlaces,
            currency.IsActive,
            null,
            null));
    }

    private static async Task<IResult> RatesAsync(
        string code,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, CurrencyReadPermission))
        {
            return Forbidden(CurrencyReadPermission, "see currencies and rates", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var currency = await context.Set<Currency>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .FirstOrDefaultAsync(c => c.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (currency is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(currency.Rates
            .OrderByDescending(static r => r.StartingDate)
            .Select(static r => new ExchangeRateView(
                r.StartingDate, r.CurrencyAmount, r.BaseAmount, r.Multiplier)));
    }

    private static async Task<IResult> SaveRateAsync(
        string code,
        SaveExchangeRateRequest request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CurrencyUpdatePermission))
        {
            return Forbidden(CurrencyUpdatePermission, "enter exchange rates", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var currency = await context.Set<Currency>()
            .Include(c => c.Rates)
            .FirstOrDefaultAsync(c => c.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (currency is null)
        {
            return Results.NotFound();
        }

        var existing = currency.Rates.FirstOrDefault(r => r.StartingDate == request.StartingDate);

        if (existing is null)
        {
            // Added through the set rather than only through the collection. Every key is handed
            // out by the constructor, and EF reads a key that is already set on a row hung off a
            // loaded parent as "this exists" -- then issues an update that matches nothing.
            context.Set<ExchangeRate>().Add(new ExchangeRate
            {
                TenantId = currency.TenantId,
                CompanyId = currency.CompanyId,
                CurrencyId = currency.Id,
                StartingDate = request.StartingDate,
                CurrencyAmount = request.CurrencyAmount,
                BaseAmount = request.BaseAmount,
            });
        }
        else
        {
            // Replaced rather than refused. Two rates starting on one day is not a state anybody
            // can resolve, and correcting a rate keyed wrong this morning is ordinary work.
            existing.CurrencyAmount = request.CurrencyAmount;
            existing.BaseAmount = request.BaseAmount;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new ExchangeRateView(
            request.StartingDate,
            request.CurrencyAmount,
            request.BaseAmount,
            request.BaseAmount / request.CurrencyAmount));
    }

    private static async Task<IResult> TaxCodesAsync(
        AsapDbContext context,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var codes = await context.Set<TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = clock.Today;

        return Results.Ok(codes.Select(c => new
        {
            code = c.Code,
            description = c.Description,
            descriptionArabic = c.DescriptionArabic,
            kind = c.Kind.ToString(),

            // Today's rate, for showing beside the code on a screen. A posting resolves the rate
            // from its own document date rather than from this.
            percentage = c.RateOn(today) ?? 0m,
        }));
    }

    private static async Task<IResult> BalanceSheetAsync(
        IDispatcher dispatcher,
        [FromQuery] DateOnly? asAt,
        [FromQuery] bool? includeAll,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var query = new BalanceSheetQuery(asAt ?? clock.Today, includeAll ?? false);

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }

    private const string ScheduleUpdatePermission = "Finance.Schedule.Update";
    private const string RecurringReadPermission = "Finance.Journal.Read";
    private const string RecurringUpdatePermission = "Finance.Journal.Create";
    private const string RecurringPostPermission = "Finance.Journal.Post";
    private const string CurrencyReadPermission = "Finance.Currency.Read";
    private const string CurrencyUpdatePermission = "Finance.Currency.Update";

    /// <summary>
    /// Whether the caller holds a permission.
    /// </summary>
    /// <remarks>
    /// Checked here rather than by the dispatcher, because these two write straight to the table
    /// instead of going through a command. Everything in this file that does go through a command
    /// is guarded by the attribute on the command itself.
    /// </remarks>
    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);
}
