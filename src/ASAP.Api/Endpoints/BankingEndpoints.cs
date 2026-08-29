using ASAP.Api.Infrastructure;
using ASAP.Modules.Finance.Banking;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>A bank account as it is reported back.</summary>
/// <param name="Id">The account key.</param>
/// <param name="Code">The short code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="BankName">The bank it is held at.</param>
/// <param name="Iban">The IBAN.</param>
/// <param name="GlAccountNo">The ledger account that stands for it.</param>
/// <param name="CurrencyCode">What it is held in, or null for the company's own.</param>
/// <param name="IsActive">Whether it may still be used.</param>
public sealed record BankAccountView(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    string? BankName,
    string? Iban,
    string GlAccountNo,
    string? CurrencyCode,
    bool IsActive);

/// <summary>A statement as it is reported back, without its lines.</summary>
/// <param name="Id">The statement key.</param>
/// <param name="No">The statement number.</param>
/// <param name="StatementDate">The day it reconciles to.</param>
/// <param name="OpeningBalance">What the bank says it opened at.</param>
/// <param name="ClosingBalance">What the bank says it closed at.</param>
/// <param name="Status">Where it has got to.</param>
/// <param name="ReconciledOn">When it was agreed.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="UnmatchedLines">How many are still unaccounted for.</param>
public sealed record BankStatementView(
    Guid Id,
    string No,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateOnly? ReconciledOn,
    int LineCount,
    int UnmatchedLines);

/// <summary>One statement line as it is reported back.</summary>
/// <param name="Id">The line key.</param>
/// <param name="TransactionDate">The day the bank says it happened.</param>
/// <param name="Description">What the bank calls it.</param>
/// <param name="Reference">The bank's own reference.</param>
/// <param name="Amount">How much, on the ledger's sign convention.</param>
/// <param name="MatchedEntryId">What in the ledger it turned out to be.</param>
/// <param name="Note">Why it has no entry, when that is the answer.</param>
public sealed record BankStatementLineView(
    Guid Id,
    DateOnly TransactionDate,
    string Description,
    string? Reference,
    decimal Amount,
    Guid? MatchedEntryId,
    string? Note);

/// <summary>One ledger entry the bank has not seen, as it is reported back.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="PostingDate">When it was posted.</param>
/// <param name="DocumentNo">What document it came from.</param>
/// <param name="Description">What it says.</param>
/// <param name="Amount">How much.</param>
public sealed record OutstandingItemView(
    Guid EntryId,
    DateOnly PostingDate,
    string? DocumentNo,
    string Description,
    decimal Amount);

/// <summary>Where a reconciliation stands, as it is reported back.</summary>
/// <param name="StatementNo">The statement.</param>
/// <param name="StatementDate">The day it reconciles to.</param>
/// <param name="ClosingBalance">What the bank says.</param>
/// <param name="LedgerBalance">What the books say.</param>
/// <param name="OutstandingTotal">What the bank has not seen.</param>
/// <param name="Difference">What is left unexplained. Nought is the only value that proves.</param>
/// <param name="UnmatchedLines">Statement lines with nothing behind them.</param>
/// <param name="Balances">Whether it may be closed.</param>
/// <param name="Outstanding">The items making up the outstanding total.</param>
public sealed record ReconciliationPositionView(
    string StatementNo,
    DateOnly StatementDate,
    decimal ClosingBalance,
    decimal LedgerBalance,
    decimal OutstandingTotal,
    decimal Difference,
    int UnmatchedLines,
    bool Balances,
    IReadOnlyList<OutstandingItemView> Outstanding);

/// <summary>What a client sends to add or change a bank account.</summary>
/// <remarks>
/// The whole record, not a patch: leaving a field out sets it to its default. Read the account,
/// change what you mean, send it back.
/// </remarks>
/// <param name="Code">The short code, which identifies it.</param>
/// <param name="Name">What it is called.</param>
/// <param name="GlAccountNo">The ledger account that stands for it.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="BankName">The bank it is held at.</param>
/// <param name="AccountNo">The account number as the bank states it.</param>
/// <param name="Iban">The IBAN.</param>
/// <param name="CurrencyCode">What it is held in, or null for the company's own.</param>
/// <param name="IsActive">Whether it may still be used.</param>
public sealed record SaveBankAccountRequest(
    string Code,
    string Name,
    string GlAccountNo,
    string? NameArabic = null,
    string? BankName = null,
    string? AccountNo = null,
    string? Iban = null,
    string? CurrencyCode = null,
    bool IsActive = true);

/// <summary>One line of a statement being entered.</summary>
/// <param name="TransactionDate">The day the bank says it happened.</param>
/// <param name="Description">What the bank calls it.</param>
/// <param name="Amount">
/// How much, on the ledger's sign convention: positive money in, negative out. A bank shows a
/// deposit as a credit because the account is its liability to you; the same deposit debits your
/// cash account. Turn the signs once, here, rather than somewhere further in.
/// </param>
/// <param name="Reference">The bank's own reference.</param>
public sealed record StatementLineRequest(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string? Reference = null);

/// <summary>What a client sends to enter a statement.</summary>
/// <param name="No">The statement number the bank gave it.</param>
/// <param name="StatementDate">The day it reconciles to.</param>
/// <param name="OpeningBalance">What the bank says it opened at.</param>
/// <param name="ClosingBalance">What the bank says it closed at.</param>
/// <param name="Lines">The lines the bank sent.</param>
public sealed record CreateStatementRequest(
    string No,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<StatementLineRequest> Lines);

/// <summary>What a client sends to say a line is a particular ledger entry.</summary>
/// <param name="EntryId">The entry it turned out to be.</param>
public sealed record MatchLineRequest(Guid EntryId);

/// <summary>Bank accounts, statements and the work of agreeing them with the ledger.</summary>
public static class BankingEndpoints
{
    private const string ReadPermission = "Finance.Bank.Read";
    private const string UpdatePermission = "Finance.Bank.Update";
    private const string ReconcilePermission = "Finance.Bank.Post";

    /// <summary>Maps the banking endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapBankingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/finance/banking").RequireAuthorization().WithTags("Finance");

        group.MapGet("/accounts", AccountsAsync)
             .WithName("BankAccounts")
             .WithSummary("Lists the bank accounts the company holds.");

        group.MapPut("/accounts/{code}", SaveAccountAsync)
             .WithName("SaveBankAccount")
             .WithSummary("Adds a bank account or replaces one, whole.");

        group.MapGet("/accounts/{code}/statements", StatementsAsync)
             .WithName("BankStatements")
             .WithSummary("Lists one account's statements, most recent first.");

        group.MapPost("/accounts/{code}/statements", CreateStatementAsync)
             .WithName("CreateBankStatement")
             .WithSummary("Enters a statement and its lines, ready to be reconciled.");

        group.MapGet("/statements/{statementId:guid}", StatementAsync)
             .WithName("BankStatement")
             .WithSummary("Reads one statement, its lines and where the reconciliation stands.");

        group.MapGet("/statements/{statementId:guid}/suggestions", SuggestAsync)
             .WithName("BankMatchSuggestions")
             .WithSummary("Says which entry each unmatched line looks like, where that is not a guess.");

        group.MapPost("/statements/lines/{lineId:guid}/match", MatchAsync)
             .WithName("MatchBankLine")
             .WithSummary("Records that a statement line is a particular ledger entry.");

        group.MapDelete("/statements/lines/{lineId:guid}/match", UnmatchAsync)
             .WithName("UnmatchBankLine")
             .WithSummary("Takes a match back off a line.");

        group.MapPost("/statements/{statementId:guid}/reconcile", ReconcileAsync)
             .WithName("ReconcileBankStatement")
             .WithSummary("Agrees a statement, if and only if the books and the bank prove out.");

        return app;
    }

    private static async Task<IResult> AccountsAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] bool? includeInactive,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see bank accounts", http);
        }

        var query = context.Set<BankAccount>().AsNoTracking();

        var accounts = await (includeInactive == true ? query : query.Where(a => a.IsActive))
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(accounts.Select(static a => new BankAccountView(
            a.Id, a.Code, a.Name, a.NameArabic, a.BankName, a.Iban, a.GlAccountNo, a.CurrencyCode, a.IsActive)));
    }

    private static async Task<IResult> SaveAccountAsync(
        string code,
        SaveBankAccountRequest request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "maintain bank accounts", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var account = await context.Set<BankAccount>()
            .FirstOrDefaultAsync(a => a.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            account = new BankAccount
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.RequireCompanyId(),
                Code = normalised,
                Name = request.Name,
                GlAccountNo = request.GlAccountNo,
            };

            context.Set<BankAccount>().Add(account);
        }

        account.Name = request.Name;
        account.NameArabic = request.NameArabic;
        account.BankName = request.BankName;
        account.AccountNo = request.AccountNo;
        account.Iban = request.Iban;
        account.GlAccountNo = request.GlAccountNo;
        account.CurrencyCode = request.CurrencyCode;
        account.IsActive = request.IsActive;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new BankAccountView(
            account.Id,
            account.Code,
            account.Name,
            account.NameArabic,
            account.BankName,
            account.Iban,
            account.GlAccountNo,
            account.CurrencyCode,
            account.IsActive));
    }

    private static async Task<IResult> StatementsAsync(
        string code,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see bank statements", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var statements = await context.Set<BankStatement>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.BankAccount!.Code == normalised)
            .OrderByDescending(s => s.StatementDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(statements.Select(Summarise));
    }

    private static async Task<IResult> CreateStatementAsync(
        string code,
        CreateStatementRequest request,
        AsapDbContext context,
        BankReconciliationService reconciliation,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "enter bank statements", http);
        }

        var normalised = code.Trim().ToUpperInvariant();

        var account = await context.Set<BankAccount>()
            .FirstOrDefaultAsync(a => a.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return Results.NotFound();
        }

        var statement = new BankStatement
        {
            TenantId = account.TenantId,
            CompanyId = account.CompanyId,
            BankAccountId = account.Id,
            No = request.No,
            StatementDate = request.StatementDate,
            OpeningBalance = request.OpeningBalance,
            ClosingBalance = request.ClosingBalance,
        };

        context.Set<BankStatement>().Add(statement);

        foreach (var line in request.Lines)
        {
            // Added through the set rather than the collection. Every key is handed out by the
            // constructor, and EF reads an already-set key on a child as "this row exists".
            context.Set<BankStatementLine>().Add(new BankStatementLine
            {
                TenantId = account.TenantId,
                CompanyId = account.CompanyId,
                BankStatementId = statement.Id,
                TransactionDate = line.TransactionDate,
                Description = line.Description,
                Amount = line.Amount,
                Reference = line.Reference,
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var position = await reconciliation
            .PositionAsync(statement.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            statement = Summarise(statement),
            position = position.Succeeded ? Render(position.Value) : null,
        });
    }

    private static async Task<IResult> StatementAsync(
        Guid statementId,
        AsapDbContext context,
        BankReconciliationService reconciliation,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see bank statements", http);
        }

        var statement = await context.Set<BankStatement>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == statementId, cancellationToken)
            .ConfigureAwait(false);

        if (statement is null)
        {
            return Results.NotFound();
        }

        var position = await reconciliation
            .PositionAsync(statementId, cancellationToken)
            .ConfigureAwait(false);

        return position.Failed
            ? Refused(position, http)
            : Results.Ok(new
            {
                statement = Summarise(statement),
                lines = statement.Lines
                    .OrderBy(static l => l.TransactionDate)
                    .Select(static l => new BankStatementLineView(
                        l.Id, l.TransactionDate, l.Description, l.Reference, l.Amount, l.MatchedEntryId, l.Note)),
                position = Render(position.Value),
            });
    }

    private static async Task<IResult> SuggestAsync(
        Guid statementId,
        BankReconciliationService reconciliation,
        IUserContext user,
        HttpContext http,
        [FromQuery] int? withinDays,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see bank statements", http);
        }

        var suggestions = await reconciliation
            .SuggestAsync(statementId, withinDays ?? 5, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(suggestions.Select(static s => new { lineId = s.LineId, entryId = s.EntryId }));
    }

    private static async Task<IResult> MatchAsync(
        Guid lineId,
        MatchLineRequest request,
        BankReconciliationService reconciliation,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "match bank statement lines", http);
        }

        var result = await reconciliation
            .MatchAsync(lineId, request.EntryId, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { messages = MessagePayload.FromAll(result.Messages) });
    }

    private static async Task<IResult> UnmatchAsync(
        Guid lineId,
        BankReconciliationService reconciliation,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "match bank statement lines", http);
        }

        var result = await reconciliation.UnmatchAsync(lineId, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { messages = MessagePayload.FromAll(result.Messages) });
    }

    private static async Task<IResult> ReconcileAsync(
        Guid statementId,
        BankReconciliationService reconciliation,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReconcilePermission))
        {
            return Forbidden(ReconcilePermission, "agree a bank statement", http);
        }

        var result = await reconciliation
            .ReconcileAsync(statementId, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                position = Render(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static BankStatementView Summarise(BankStatement statement)
        => new(
            statement.Id,
            statement.No,
            statement.StatementDate,
            statement.OpeningBalance,
            statement.ClosingBalance,
            statement.Status.ToString(),
            statement.ReconciledOn,
            statement.Lines.Count,
            statement.Lines.Count(static l => !l.IsMatched));

    private static ReconciliationPositionView Render(ReconciliationPosition position)
        => new(
            position.StatementNo,
            position.StatementDate,
            position.ClosingBalance,
            position.LedgerBalance,
            position.OutstandingTotal,
            position.Difference,
            position.UnmatchedLines,
            position.Balances,
            [
                .. position.Outstanding.Select(static o => new OutstandingItemView(
                    o.EntryId, o.PostingDate, o.DocumentNo, o.Description, o.Amount)),
            ]);

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
