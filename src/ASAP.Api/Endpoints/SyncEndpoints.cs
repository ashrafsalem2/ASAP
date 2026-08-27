using ASAP.Api.Infrastructure;
using ASAP.Platform.Core.Sync;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>What a branch sends to say it has applied a page.</summary>
/// <param name="BranchId">The branch, or null for the one the caller is signed in to.</param>
/// <param name="Sequence">What it has applied up to.</param>
public sealed record AcknowledgeSyncRequest(long Sequence, Guid? BranchId = null);

/// <summary>What a branch sends to push a document it has already posted locally.</summary>
/// <param name="IdempotencyKey">
/// What the caller calls this attempt. Unique per branch, and the whole mechanism: a push
/// carrying a key already seen returns the original outcome rather than posting again.
/// </param>
/// <param name="DocumentType">What kind of document it is, for example <c>Pos.Receipt</c>.</param>
/// <param name="DocumentNo">The number it carries.</param>
/// <param name="HeldReason">Why it cannot be applied yet, when the branch knows it cannot.</param>
/// <param name="BranchId">The branch, or null for the one the caller is signed in to.</param>
public sealed record PushDocumentRequest(
    string IdempotencyKey,
    string DocumentType,
    string? DocumentNo = null,
    string? HeldReason = null,
    Guid? BranchId = null);

/// <summary>
/// The branch synchronisation contract: master data down, transactions up.
/// </summary>
/// <remarks>
/// The asymmetry is the design rather than a convention. See
/// docs/architecture/branch-synchronisation.md.
/// </remarks>
public static class SyncEndpoints
{
    private const string ReadPermission = "Platform.Sync.Read";
    private const string SyncPermission = "Platform.Sync.Execute";

    /// <summary>Maps the synchronisation endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/sync").RequireAuthorization().WithTags("Synchronisation");

        group.MapGet("/contract", ContractAsync)
             .WithName("SyncContract")
             .WithSummary("Lists what synchronises and which way it travels.");

        group.MapGet("/changes", PullAsync)
             .WithName("SyncPull")
             .WithSummary("Everything that changed after a cursor, in order.");

        group.MapPost("/changes/acknowledge", AcknowledgeAsync)
             .WithName("SyncAcknowledge")
             .WithSummary("Records that a branch has applied everything up to a sequence.");

        group.MapPost("/documents", PushAsync)
             .WithName("SyncPush")
             .WithSummary("Takes a document from a branch, once, keyed by what the caller called the attempt.");

        group.MapGet("/status", StatusAsync)
             .WithName("SyncStatus")
             .WithSummary("Which branches are behind, and by how much.");

        return app;
    }

    private static IResult ContractAsync(SyncRegistry registry, IUserContext user, HttpContext http)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read the synchronisation contract", http);
        }

        return Results.Ok(registry.All.Select(static d => new
        {
            entityType = d.EntityType,
            direction = d.Direction.ToString(),
            module = d.Module,
        }));
    }

    private static async Task<IResult> PullAsync(
        SyncService sync,
        IUserContext user,
        HttpContext http,
        [FromQuery] long since,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SyncPermission))
        {
            return Forbidden(SyncPermission, "pull changes", http);
        }

        var page = await sync
            .PullAsync(since, pageSize ?? SyncService.MaxPageSize, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            changes = page.Changes.Select(static c => new
            {
                sequence = c.Sequence,
                entityType = c.EntityType,
                entityId = c.EntityId,
                displayNo = c.DisplayNo,
                operation = c.Operation.ToString(),
                occurredAtUtc = c.OccurredAtUtc,
            }),
            cursor = page.Cursor,
            hasMore = page.HasMore,
        });
    }

    private static async Task<IResult> AcknowledgeAsync(
        AcknowledgeSyncRequest request,
        SyncService sync,
        ITenantContext tenantContext,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SyncPermission))
        {
            return Forbidden(SyncPermission, "acknowledge changes", http);
        }

        if (BranchOf(request.BranchId, tenantContext) is not { } branchId)
        {
            return NoBranch(http);
        }

        var status = await sync
            .AcknowledgeAsync(branchId, request.Sequence, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(status);
    }

    private static async Task<IResult> PushAsync(
        PushDocumentRequest request,
        SyncService sync,
        ITenantContext tenantContext,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SyncPermission))
        {
            return Forbidden(SyncPermission, "push documents", http);
        }

        if (BranchOf(request.BranchId, tenantContext) is not { } branchId)
        {
            return NoBranch(http);
        }

        var result = await sync
            .PushAsync(
                branchId,
                request.IdempotencyKey,
                request.DocumentType,
                request.DocumentNo,
                request.HeldReason,
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> StatusAsync(
        SyncService sync,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read synchronisation status", http);
        }

        return Results.Ok(new
        {
            head = await sync.HeadAsync(cancellationToken).ConfigureAwait(false),
            branches = await sync.StatusAsync(cancellationToken).ConfigureAwait(false),
        });
    }

    /// <summary>
    /// Which branch is being spoken for.
    /// </summary>
    /// <remarks>
    /// The signed-in branch by default, because a till syncing for itself should not have to say
    /// which one it is. Naming another is allowed for head office tooling and is the only reason
    /// the parameter exists.
    /// </remarks>
    private static Guid? BranchOf(Guid? requested, ITenantContext tenantContext)
        => requested ?? tenantContext.BranchId;

    private static IResult NoBranch(HttpContext http)
        => Results.Json(
            new
            {
                type = "https://asap-erp.com/problems/message",
                title = "No branch to synchronise",
                status = StatusCodes.Status422UnprocessableEntity,
                detail = "The caller is not signed in to a branch and did not name one, so there "
                    + "is nothing to record a cursor against.",
                instance = http.Request.Path.Value,
                code = "SYNC.NO_BRANCH",
                resolution = "Sign in at the branch, or name it explicitly.",
            },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);
}
