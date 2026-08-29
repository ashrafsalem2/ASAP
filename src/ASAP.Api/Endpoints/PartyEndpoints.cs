using ASAP.Api.Infrastructure;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>A customer or vendor as it is reported back.</summary>
/// <param name="No">The party number.</param>
/// <param name="Name">The party name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="PaymentTermsDays">Days after the document date payment falls due.</param>
/// <param name="CreditLimit">The most they may owe, or zero for no limit.</param>
/// <param name="Balance">What they owe now.</param>
/// <param name="IsOverLimit">Whether the balance exceeds the limit.</param>
/// <param name="ControlAccountNo">The account they post to, when overridden.</param>
/// <param name="IsBlocked">Whether they are withdrawn from use.</param>
/// <param name="Email">Contact email.</param>
/// <param name="Phone">Contact telephone.</param>
public sealed record PartyView(
    string No,
    string Name,
    string? NameArabic,
    int PaymentTermsDays,
    decimal CreditLimit,
    decimal Balance,
    bool IsOverLimit,
    string? ControlAccountNo,
    bool IsBlocked,
    string? Email,
    string? Phone);

/// <summary>One entry on a party's account.</summary>
/// <param name="Id">The entry key, used when applying.</param>
/// <param name="PostingDate">When it was posted.</param>
/// <param name="DueDate">When it falls due.</param>
/// <param name="TransactionNo">The transaction it belongs to.</param>
/// <param name="DocumentType">What kind of document produced it.</param>
/// <param name="DocumentNo">The document number.</param>
/// <param name="ExternalDocumentNo">The other side's reference.</param>
/// <param name="Description">What it says on a statement.</param>
/// <param name="Amount">The signed amount.</param>
/// <param name="RemainingAmount">What is still unsettled.</param>
/// <param name="IsOpen">Whether anything is still outstanding.</param>
/// <param name="DaysOverdue">How late it is today, or zero when not yet due.</param>
/// <param name="CurrencyCode">What it was written in, or null for the company's own currency.</param>
/// <param name="AmountInCurrency">The amount as written, before conversion.</param>
/// <param name="RemainingAmountInCurrency">
/// What is still outstanding in that currency, which for a foreign entry is what actually decides
/// whether anybody still owes anything. The company-currency figure beside it moves with the rate
/// and says nothing about the debt.
/// </param>
public sealed record PartyLedgerEntryView(
    Guid Id,
    DateOnly PostingDate,
    DateOnly DueDate,
    long TransactionNo,
    string DocumentType,
    string? DocumentNo,
    string? ExternalDocumentNo,
    string Description,
    decimal Amount,
    decimal RemainingAmount,
    bool IsOpen,
    int DaysOverdue,
    string? CurrencyCode = null,
    decimal? AmountInCurrency = null,
    decimal? RemainingAmountInCurrency = null);

/// <summary>What a client sends to settle one entry against another.</summary>
/// <param name="FromEntryId">The entry the money comes from, normally a payment.</param>
/// <param name="ToEntryId">The entry being settled, normally an invoice.</param>
/// <param name="Amount">How much to apply, or null for as much as both sides allow.</param>
public sealed record ApplyEntriesRequest(Guid FromEntryId, Guid ToEntryId, decimal? Amount = null);

/// <summary>Customers, vendors, their ledgers and what they owe.</summary>
public static class PartyEndpoints
{
    private const string ReadPermission = "Finance.Party.Read";
    private const string ApplyPermission = "Finance.Party.Post";

    /// <summary>Maps the customer and vendor endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/finance").RequireAuthorization().WithTags("Finance");

        group.MapGet("/customers", (AsapDbContext c, IUserContext u, HttpContext h, CancellationToken t)
                 => PartiesAsync<Customer>(c, u, h, t))
             .WithName("Customers")
             .WithSummary("Lists customers with what each owes.");

        group.MapGet("/vendors", (AsapDbContext c, IUserContext u, HttpContext h, CancellationToken t)
                 => PartiesAsync<Vendor>(c, u, h, t))
             .WithName("Vendors")
             .WithSummary("Lists vendors with what is owed to each.");

        group.MapGet("/customers/{partyNo}/entries",
                 (string partyNo, bool? openOnly, AsapDbContext c, IUserContext u, IClock k, HttpContext h, CancellationToken t)
                     => EntriesAsync<CustomerLedgerEntry>(partyNo, openOnly, c, u, k, h, t))
             .WithName("CustomerLedgerEntries")
             .WithSummary("Lists one customer's ledger entries, most recent first.");

        group.MapGet("/vendors/{partyNo}/entries",
                 (string partyNo, bool? openOnly, AsapDbContext c, IUserContext u, IClock k, HttpContext h, CancellationToken t)
                     => EntriesAsync<VendorLedgerEntry>(partyNo, openOnly, c, u, k, h, t))
             .WithName("VendorLedgerEntries")
             .WithSummary("Lists one vendor's ledger entries, most recent first.");

        group.MapPost("/customers/apply",
                 (ApplyEntriesRequest r, PartyApplicationService s, IUserContext u, HttpContext h, CancellationToken t)
                     => ApplyAsync(PartyKind.Customer, r, s, u, h, t))
             .WithName("ApplyCustomerEntries")
             .WithSummary("Records which payment settled which customer invoice.");

        group.MapPost("/vendors/apply",
                 (ApplyEntriesRequest r, PartyApplicationService s, IUserContext u, HttpContext h, CancellationToken t)
                     => ApplyAsync(PartyKind.Vendor, r, s, u, h, t))
             .WithName("ApplyVendorEntries")
             .WithSummary("Records which payment settled which vendor invoice.");

        group.MapPost("/customers/unapply/{applicationId:guid}",
                 (Guid applicationId, PartyApplicationService s, IUserContext u, HttpContext h, CancellationToken t)
                     => UnapplyAsync(PartyKind.Customer, applicationId, s, u, h, t))
             .WithName("UnapplyCustomerEntries")
             .WithSummary("Undoes a customer application, giving both entries back what it took.");

        group.MapPost("/vendors/unapply/{applicationId:guid}",
                 (Guid applicationId, PartyApplicationService s, IUserContext u, HttpContext h, CancellationToken t)
                     => UnapplyAsync(PartyKind.Vendor, applicationId, s, u, h, t))
             .WithName("UnapplyVendorEntries")
             .WithSummary("Undoes a vendor application.");

        group.MapGet("/reports/aged-analysis", AgedAnalysisAsync)
             .WithName("AgedAnalysis")
             .WithSummary("Reports what is outstanding, split by how late it is.");

        return app;
    }

    private static async Task<IResult> PartiesAsync<TParty>(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        where TParty : Party
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view customers and vendors", http);
        }

        var parties = await context.Set<TParty>()
            .AsNoTracking()
            .OrderBy(p => p.No)
            .Select(p => new PartyView(
                p.No,
                p.Name,
                p.NameArabic,
                p.PaymentTermsDays,
                p.CreditLimit,
                p.Balance,
                p.CreditLimit > 0m && p.Balance > p.CreditLimit,
                p.ControlAccountNo,
                p.IsBlocked,
                p.Email,
                p.Phone))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(parties);
    }

    private static async Task<IResult> EntriesAsync<TEntry>(
        string partyNo,
        bool? openOnly,
        AsapDbContext context,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view customers and vendors", http);
        }

        var query = context.Set<TEntry>().AsNoTracking().Where(e => e.PartyNo == partyNo);

        if (openOnly == true)
        {
            query = query.Where(e => e.IsOpen);
        }

        var entries = await query
            .OrderByDescending(e => e.PostingDate)
            .ThenByDescending(e => e.TransactionNo)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = clock.Today;

        return Results.Ok(entries.Select(e => new PartyLedgerEntryView(
            e.Id,
            e.PostingDate,
            e.DueDate,
            e.TransactionNo,
            e.DocumentType.ToString(),
            e.DocumentNo,
            e.ExternalDocumentNo,
            e.Description,
            e.Amount,
            e.RemainingAmount,
            e.IsOpen,

            // Only meaningful while something is still owed. A settled invoice that was paid late
            // is history, and colouring it red on a statement helps nobody.
            e.IsOpen ? e.DaysOverdue(today) : 0,
            e.CurrencyCode,
            e.AmountInCurrency,
            e.RemainingAmountInCurrency)));
    }

    private static async Task<IResult> ApplyAsync(
        PartyKind kind,
        ApplyEntriesRequest request,
        PartyApplicationService applications,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ApplyPermission))
        {
            return Forbidden(ApplyPermission, "apply payments to invoices", http);
        }

        var result = await applications
            .ApplyAsync(kind, request.FromEntryId, request.ToEntryId, request.Amount, cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        return Results.Ok(new
        {
            appliedAmount = result.Value.AppliedAmount,
            fromRemaining = result.Value.FromRemaining,
            toRemaining = result.Value.ToRemaining,
            closedEntries = result.Value.ClosedEntries,
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static async Task<IResult> UnapplyAsync(
        PartyKind kind,
        Guid applicationId,
        PartyApplicationService applications,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ApplyPermission))
        {
            return Forbidden(ApplyPermission, "apply payments to invoices", http);
        }

        var result = await applications
            .UnapplyAsync(kind, applicationId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        return Results.Ok(new
        {
            appliedAmount = result.Value.AppliedAmount,
            fromRemaining = result.Value.FromRemaining,
            toRemaining = result.Value.ToRemaining,
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static async Task<IResult> AgedAnalysisAsync(
        IDispatcher dispatcher,
        [FromQuery] string? kind,
        [FromQuery] DateOnly? asAt,
        [FromQuery] string? bands,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var wanted = Enum.TryParse<PartyKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : PartyKind.Customer;

        var query = new AgedAnalysisQuery(wanted, asAt ?? clock.Today, ParseBands(bands));

        return Results.Ok(await dispatcher.SendAsync(query, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads a band list such as <c>30,60,90</c>, ignoring anything that is not a number.</summary>
    private static List<int>? ParseBands(string? bands)
    {
        if (string.IsNullOrWhiteSpace(bands))
        {
            return null;
        }

        var parsed = bands
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => int.TryParse(part, out var days) ? days : 0)
            .Where(static days => days > 0)
            .ToList();

        return parsed.Count > 0 ? parsed : null;
    }

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);
}
