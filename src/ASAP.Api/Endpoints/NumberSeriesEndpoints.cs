using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>One dated range of numbers a series issues from.</summary>
/// <param name="StartingDate">The first day this line applies from.</param>
/// <param name="StartingNumber">The first number, whose shape sets the pattern for the rest.</param>
/// <param name="EndingNumber">The last number the line may reach, or null for no ceiling.</param>
/// <param name="Increment">How much to add each time.</param>
/// <param name="WarnWhenRemainingBelow">Warn once this few are left, or null never to warn.</param>
/// <param name="IsOpen">Whether the line may still issue.</param>
public sealed record SaveNumberSeriesLineRequest(
    DateOnly StartingDate,
    string StartingNumber,
    string? EndingNumber = null,
    int Increment = 1,
    int? WarnWhenRemainingBelow = null,
    bool IsOpen = true);

/// <summary>What a client sends to create or change a number series.</summary>
/// <param name="Code">Its code, which the settings refer to.</param>
/// <param name="Description">What it numbers.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="AllowGaps">
/// Whether a number may be taken and not used. Off means the series is gapless, which a tax
/// invoice sequence has to be and an internal order number does not.
/// </param>
/// <param name="AllowManualEntry">Whether somebody may type a number instead of taking the next.</param>
/// <param name="EnforceDateOrder">Whether numbers must be issued in date order.</param>
/// <param name="IsActive">Whether it is still in use.</param>
/// <param name="Lines">Its dated ranges. The whole set, replacing what was there.</param>
public sealed record SaveNumberSeriesRequest(
    string Code,
    string Description,
    string? DescriptionArabic = null,
    bool AllowGaps = true,
    bool AllowManualEntry = false,
    bool EnforceDateOrder = false,
    bool IsActive = true,
    IReadOnlyList<SaveNumberSeriesLineRequest>? Lines = null);

/// <summary>
/// The series every document number comes out of.
/// </summary>
/// <remarks>
/// <para>
/// Declared in settings all over the system — <c>Hr.Payroll.NumberSeries</c>,
/// <c>Pos.Receipt.NumberSeries</c> and a dozen more — and until now there was no way to see one,
/// let alone add a line for next year. A series whose last line ends in December stops the shop
/// trading on the first of January, and finding that out on the day is the whole reason this
/// screen exists.
/// </para>
/// <para>
/// Whether a series may have gaps is the setting that matters. A simplified tax invoice sequence
/// with holes in it is a question from the authority; an internal transfer number with holes in
/// it is nothing at all.
/// </para>
/// </remarks>
public static class NumberSeriesEndpoints
{
    private const string ReadPermission = "Platform.NumberSeries.Read";
    private const string CreatePermission = "Platform.NumberSeries.Create";
    private const string UpdatePermission = "Platform.NumberSeries.Update";

    /// <summary>Maps the number series endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapNumberSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/number-series").RequireAuthorization().WithTags("Numbering");

        group.MapGet("/", ListAsync)
             .WithName("NumberSeriesList")
             .WithSummary("Lists the series, their lines, and how many numbers each has left.");

        group.MapPost("/", SaveAsync)
             .WithName("SaveNumberSeries")
             .WithSummary("Creates a series, or rewrites one that exists.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "see number series", http);
        }

        var series = await context.Set<NumberSeries>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .OrderBy(static s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(series.Select(static s => new
        {
            code = s.Code,
            description = s.Description,
            descriptionArabic = s.DescriptionArabic,
            allowGaps = s.AllowGaps,
            allowManualEntry = s.AllowManualEntry,
            enforceDateOrder = s.EnforceDateOrder,
            isActive = s.IsActive,
            lines = s.Lines
                .OrderBy(static l => l.StartingDate)
                .Select(static l => new
                {
                    startingDate = l.StartingDate,
                    startingNumber = l.StartingNumber,
                    endingNumber = l.EndingNumber,
                    lastNumberUsed = l.LastNumberUsed,
                    lastDateUsed = l.LastDateUsed,
                    increment = l.Increment,
                    warnWhenRemainingBelow = l.WarnWhenRemainingBelow,
                    isOpen = l.IsOpen,

                    // The number worth looking at. A line with none left is a shop that stops
                    // trading on the day it runs out, and nobody notices until it does.
                    remaining = l.Remaining(),
                }),
        }));
    }

    private static async Task<IResult> SaveAsync(
        SaveNumberSeriesRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.Set<NumberSeries>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Code == request.Code, cancellationToken)
            .ConfigureAwait(false);

        var permission = existing is null ? CreatePermission : UpdatePermission;

        if (!Can(user, permission))
        {
            return Forbidden(permission, "maintain number series", http);
        }

        var series = existing;

        if (series is null)
        {
            series = new NumberSeries
            {
                TenantId = tenant.TenantId ?? Guid.Empty,
                CompanyId = tenant.RequireCompanyId(),
                Code = request.Code,
                Description = request.Description,
            };

            context.Set<NumberSeries>().Add(series);
        }

        series.Description = request.Description;
        series.DescriptionArabic = request.DescriptionArabic;
        series.AllowGaps = request.AllowGaps;
        series.AllowManualEntry = request.AllowManualEntry;
        series.EnforceDateOrder = request.EnforceDateOrder;
        series.IsActive = request.IsActive;

        var lines = request.Lines ?? [];

        foreach (var line in lines)
        {
            var match = series.Lines.FirstOrDefault(l => l.StartingDate == line.StartingDate);

            if (match is null)
            {
                // Added through the set. Every key here comes from the constructor, and a child
                // hung off a parent that was loaded reads to EF as a row that already exists.
                match = new NumberSeriesLine
                {
                    TenantId = series.TenantId,
                    CompanyId = series.CompanyId,
                    NumberSeriesId = series.Id,
                    StartingDate = line.StartingDate,
                    StartingNumber = line.StartingNumber,
                };

                context.Set<NumberSeriesLine>().Add(match);
                series.Lines.Add(match);
            }

            // What has already been issued is never rewritten. It is the record of which numbers
            // are gone, and a series that forgot it would hand the same invoice number out twice.
            match.StartingNumber = match.LastNumberUsed is null
                ? line.StartingNumber
                : match.StartingNumber;

            match.EndingNumber = line.EndingNumber;
            match.Increment = line.Increment;
            match.WarnWhenRemainingBelow = line.WarnWhenRemainingBelow;
            match.IsOpen = line.IsOpen;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = series.Code, created = existing is null, lines = series.Lines.Count });
    }

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            Infrastructure.AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);
}
