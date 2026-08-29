using ASAP.Api.Infrastructure;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>One value a dimension may take, as it is written and read back.</summary>
/// <param name="Code">The short code, for example <c>SALES</c>.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Kind">Standard, Heading or Total.</param>
/// <param name="TotalRange">For a total, the range it sums.</param>
/// <param name="Indentation">How far to indent it in the list.</param>
/// <param name="IsBlocked">Whether it may still be posted to.</param>
public sealed record DimensionValueView(
    string Code,
    string Name,
    string? NameArabic = null,
    string Kind = "Standard",
    string? TotalRange = null,
    int Indentation = 0,
    bool IsBlocked = false);

/// <summary>A dimension and its values.</summary>
/// <param name="Code">The short code, for example <c>DEPARTMENT</c>.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Values">The values it may take.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Description">What the axis is for.</param>
/// <param name="ShortcutIndex">
/// Its position among the shortcut dimensions, 1 to 8, or null for an ordinary one. A shortcut is
/// copied onto every ledger entry, so filtering a million entries by it is a seek rather than a
/// join. Two axes usually earn that; using all eight defeats the point.
/// </param>
/// <param name="IsMandatory">Whether every transaction must carry a value for it.</param>
/// <param name="IsBlocked">Whether it may still be used.</param>
public sealed record DimensionView(
    string Code,
    string Name,
    IReadOnlyList<DimensionValueView> Values,
    string? NameArabic = null,
    string? Description = null,
    int? ShortcutIndex = null,
    bool IsMandatory = false,
    bool IsBlocked = false);

/// <summary>The axes a company analyses its figures along.</summary>
public static class DimensionEndpoints
{
    private const string ReadPermission = "Platform.Dimension.Read";
    private const string UpdatePermission = "Platform.Dimension.Update";

    /// <summary>Maps the dimension endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDimensionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/dimensions").RequireAuthorization().WithTags("Setup");

        group.MapGet("/", ListAsync)
             .WithName("Dimensions")
             .WithSummary("Lists the dimensions this company analyses by, and their values.");

        group.MapPut("/{code}", SaveAsync)
             .WithName("SaveDimension")
             .WithSummary("Adds a dimension or replaces one, values and all.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] bool? includeBlocked,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see dimensions", http);
        }

        var query = context.Set<Dimension>().AsNoTracking().Include(d => d.Values);

        var dimensions = await (includeBlocked == true ? query : query.Where(d => !d.IsBlocked))
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(dimensions.Select(Render));
    }

    private static async Task<IResult> SaveAsync(
        string code,
        DimensionView request,
        AsapDbContext context,
        IUserContext user,
        ITenantContext tenantContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "maintain dimensions", http);
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

                var dimension = await context.Set<Dimension>()
                    .Include(d => d.Values)
                    .FirstOrDefaultAsync(d => d.Code == normalised, cancellationToken)
                    .ConfigureAwait(false);

                if (dimension is null)
                {
                    dimension = new Dimension
                    {
                        TenantId = tenantContext.TenantId ?? Guid.Empty,
                        CompanyId = tenantContext.RequireCompanyId(),
                        Code = normalised,
                        Name = request.Name,
                    };

                    context.Set<Dimension>().Add(dimension);
                }

                dimension.Name = request.Name;
                dimension.NameArabic = request.NameArabic;
                dimension.Description = request.Description;
                dimension.ShortcutIndex = request.ShortcutIndex;
                dimension.IsMandatory = request.IsMandatory;
                dimension.IsBlocked = request.IsBlocked;

                // Values are replaced rather than merged, and in two writes for the same reason a
                // statement layout's rows are: codes are unique per dimension, so a single write
                // would offer the new SALES before it had taken the old one away.
                //
                // A value that has been posted against is kept by the database's own foreign keys
                // rather than by anything here, so an attempt to delete one in use is refused
                // rather than silently orphaning history.
                context.Set<DimensionValue>().RemoveRange(dimension.Values);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var value in request.Values)
                {
                    context.Set<DimensionValue>().Add(new DimensionValue
                    {
                        TenantId = dimension.TenantId,
                        CompanyId = dimension.CompanyId,
                        DimensionId = dimension.Id,
                        Code = value.Code.Trim().ToUpperInvariant(),
                        Name = value.Name,
                        NameArabic = value.NameArabic,
                        Kind = Enum.TryParse<DimensionValueKind>(value.Kind, true, out var kind)
                            ? kind
                            : DimensionValueKind.Standard,
                        TotalRange = value.TotalRange,
                        Indentation = value.Indentation,
                        IsBlocked = value.IsBlocked,
                    });
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return Results.Ok(new { code = normalised, values = request.Values.Count });
    }

    private static DimensionView Render(Dimension dimension)
        => new(
            dimension.Code,
            dimension.Name,
            [
                .. dimension.Values
                    .OrderBy(static v => v.Code)
                    .Select(static v => new DimensionValueView(
                        v.Code,
                        v.Name,
                        v.NameArabic,
                        v.Kind.ToString(),
                        v.TotalRange,
                        v.Indentation,
                        v.IsBlocked)),
            ],
            dimension.NameArabic,
            dimension.Description,
            dimension.ShortcutIndex,
            dimension.IsMandatory,
            dimension.IsBlocked);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);
}
