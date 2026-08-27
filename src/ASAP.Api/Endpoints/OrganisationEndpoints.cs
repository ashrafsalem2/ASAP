using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

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

        group.MapGet("/branches", BranchesAsync)
             .WithName("Branches")
             .WithSummary("Lists the branches of the company the caller is working in.");

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
                isActive = c.IsActive,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { current = tenant.CompanyId, companies });
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
                isActive = b.IsActive,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(branches);
    }
}
