using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>
/// What a client sends to create or change a company.
/// </summary>
/// <remarks>
/// The whole record, every time, as with a branch: a field left out is set to nothing rather
/// than left alone.
/// </remarks>
/// <param name="Code">Its code, which appears on documents.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its Arabic name.</param>
/// <param name="BaseCurrencyCode">What it reports in.</param>
/// <param name="RegistrationNo">Commercial registration.</param>
/// <param name="TaxRegistrationNo">Tax registration, which appears on every invoice.</param>
/// <param name="FiscalYearStartMonth">Which month its financial year opens in.</param>
/// <param name="IsActive">Whether it may be worked in.</param>
public sealed record SaveCompanyRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string BaseCurrencyCode = "SAR",
    string? RegistrationNo = null,
    string? TaxRegistrationNo = null,
    int FiscalYearStartMonth = 1,
    bool IsActive = true);

/// <summary>
/// What a client sends to create or change a branch.
/// </summary>
/// <remarks>
/// The whole record, every time. A field left out is a field set to nothing rather than a field
/// left alone, which is worth knowing before somebody writes a script that changes one thing.
/// </remarks>
/// <param name="Code">Its code, which appears on documents and in every branch report.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its Arabic name.</param>
/// <param name="Kind">Head office, shop, warehouse or office.</param>
/// <param name="City">Where it is.</param>
/// <param name="Address">Its address.</param>
/// <param name="Phone">Its telephone number.</param>
/// <param name="IsActive">Whether it is still trading.</param>
public sealed record SaveBranchRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    BranchKind Kind = BranchKind.Store,
    string? City = null,
    string? Address = null,
    string? Phone = null,
    bool IsActive = true);

/// <summary>
/// The companies and branches a screen needs in order to name one.
/// </summary>
/// <remarks>
/// <para>
/// Almost every document in this system carries a branch, and until now every screen showing one
/// had a key and no name for it. A payroll that charges 6,193.55 to
/// <c>01a03d5f-d075-71c1-9119-beacba8df2e2</c> is not a report anybody can read.
/// </para>
/// <para>
/// Reading the list needs no permission beyond being signed in. Which shops a company has is on
/// the front of each of them, and requiring a permission for the names would mean every screen
/// that mentions a branch refusing anybody who lacked it — which is every screen.
/// </para>
/// </remarks>
public static class OrganisationEndpoints
{
    /// <summary>Maps the company and branch endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api").RequireAuthorization().WithTags("Organisation");

        group.MapGet("/companies", CompaniesAsync)
             .WithName("Companies")
             .WithSummary("Lists the companies in this tenant.");

        group.MapPost("/companies", SaveCompanyAsync)
             .WithName("SaveCompany")
             .WithSummary("Creates a company, or changes one that exists.");

        group.MapGet("/branches", BranchesAsync)
             .WithName("Branches")
             .WithSummary("Lists the branches of the company the caller is working in.");

        group.MapPost("/branches", SaveBranchAsync)
             .WithName("SaveBranch")
             .WithSummary("Opens a branch, or changes one that exists.");

        return app;
    }

    private static async Task<IResult> CompaniesAsync(
        AsapDbContext context,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var companies = await context.Companies
            .AsNoTracking()
            .OrderBy(static c => c.Code)
            .Select(static c => new
            {
                id = c.Id,
                code = c.Code,
                name = c.Name,
                nameArabic = c.NameArabic,
                baseCurrencyCode = c.BaseCurrencyCode,
                registrationNo = c.RegistrationNo,
                taxRegistrationNo = c.TaxRegistrationNo,
                fiscalYearStartMonth = c.FiscalYearStartMonth,

                // Says whether the currency and the year's opening month may still be changed,
                // decided by the server so the screen cannot disagree with the endpoint.
                hasPostedEntries = c.HasPostedEntries,
                isActive = c.IsActive,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { current = tenant.CompanyId, companies });
    }

    /// <summary>
    /// Creates a company or changes one that exists, matched on its code.
    /// </summary>
    /// <remarks>
    /// The base currency and the month the year opens in are settled when the company is created
    /// and not afterwards. Both decide how every figure already posted was measured, and changing
    /// either would leave the history saying something it never meant.
    /// </remarks>
    private static async Task<IResult> SaveCompanyAsync(
        SaveCompanyRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.Companies
            .FirstOrDefaultAsync(c => c.Code == request.Code, cancellationToken)
            .ConfigureAwait(false);

        var permission = existing is null ? "Platform.Company.Create" : "Platform.Company.Update";

        if (!Can(user, permission))
        {
            return Forbidden(permission, "maintain companies", http);
        }

        if (existing is null)
        {
            context.Companies.Add(new Company
            {
                TenantId = tenant.TenantId ?? Guid.Empty,
                Code = request.Code,
                Name = request.Name,
                NameArabic = request.NameArabic,
                BaseCurrencyCode = request.BaseCurrencyCode,
                RegistrationNo = request.RegistrationNo,
                TaxRegistrationNo = request.TaxRegistrationNo,
                FiscalYearStartMonth = request.FiscalYearStartMonth,
                IsActive = request.IsActive,
            });
        }
        else
        {
            existing.Name = request.Name;
            existing.NameArabic = request.NameArabic;
            existing.RegistrationNo = request.RegistrationNo;
            existing.TaxRegistrationNo = request.TaxRegistrationNo;
            existing.IsActive = request.IsActive;

            // Currency and the year's opening month are left alone once anything has been posted.
            // They describe how the existing figures were measured, not a preference.
            if (!existing.HasPostedEntries)
            {
                existing.BaseCurrencyCode = request.BaseCurrencyCode;
                existing.FiscalYearStartMonth = request.FiscalYearStartMonth;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = request.Code, created = existing is null });
    }

    /// <summary>
    /// Opens a branch or changes one that exists, matched on its code.
    /// </summary>
    /// <remarks>
    /// A branch that has closed is made inactive rather than removed: last year's documents point
    /// at it, and a branch report for last year has to be able to name it. The one thing refused
    /// is closing the last active branch, because every document is posted somewhere.
    /// </remarks>
    private static async Task<IResult> SaveBranchAsync(
        SaveBranchRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.Branches
            .FirstOrDefaultAsync(b => b.Code == request.Code, cancellationToken)
            .ConfigureAwait(false);

        var permission = existing is null ? "Platform.Branch.Create" : "Platform.Branch.Update";

        if (!Can(user, permission))
        {
            return Forbidden(permission, "maintain branches", http);
        }

        if (existing is not null && existing.IsActive && !request.IsActive)
        {
            var othersOpen = await context.Branches
                .CountAsync(b => b.Id != existing.Id && b.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (othersOpen == 0)
            {
                return Refused(
                    messages.Render(
                        PlatformMessages.LastActiveBranch,
                        Args(("Code", request.Code))),
                    http);
            }
        }

        if (existing is null)
        {
            context.Branches.Add(new Branch
            {
                TenantId = tenant.TenantId ?? Guid.Empty,
                CompanyId = tenant.RequireCompanyId(),
                Code = request.Code,
                Name = request.Name,
                NameArabic = request.NameArabic,
                Kind = request.Kind,
                City = request.City,
                Address = request.Address,
                Phone = request.Phone,
                IsActive = request.IsActive,
            });
        }
        else
        {
            existing.Name = request.Name;
            existing.NameArabic = request.NameArabic;
            existing.Kind = request.Kind;
            existing.City = request.City;
            existing.Address = request.Address;
            existing.Phone = request.Phone;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = request.Code, created = existing is null });
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            Infrastructure.AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(AsapMessage message, HttpContext http)
    {
        var result = Result.Failure(message);

        return Results.Json(
            Infrastructure.AsapProblem.From(
                result,
                Infrastructure.AsapProblem.StatusFor(result.Messages),
                http.Request.Path),
            statusCode: Infrastructure.AsapProblem.StatusFor(result.Messages));
    }

    private static async Task<IResult> BranchesAsync(
        AsapDbContext context,
        [FromQuery] bool? includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.Branches.AsNoTracking();

        // A branch that has closed still owns last year's transactions, so it is filtered out of
        // the pickers rather than removed. Reports asking about it say so explicitly.
        if (includeInactive != true)
        {
            query = query.Where(static b => b.IsActive);
        }

        var branches = await query
            .OrderBy(static b => b.Code)
            .Select(static b => new
            {
                id = b.Id,
                code = b.Code,
                name = b.Name,
                nameArabic = b.NameArabic,
                kind = b.Kind.ToString(),
                city = b.City,
                address = b.Address,
                phone = b.Phone,
                isActive = b.IsActive,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(branches);
    }
}
