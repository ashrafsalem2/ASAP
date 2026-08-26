using System.Collections.Concurrent;
using System.Reflection;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Cqrs;

/// <summary>
/// Refuses a request whose caller lacks the permission it declares.
/// </summary>
/// <remarks>
/// <para>
/// Runs first in the pipeline, before anything opens a transaction or reads a row. A request that
/// was going to be refused should cost as little as possible.
/// </para>
/// <para>
/// Because the requirement is declared on the request rather than checked inside the handler,
/// there is one visible place per operation stating what it needs, and a handler cannot quietly
/// forget to check. <see cref="PermissionAudit"/> walks every registered request at startup and
/// reports any that declare nothing.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request being guarded.</typeparam>
/// <typeparam name="TResponse">What answering it produces.</typeparam>
/// <param name="userContext">Who is asking, and what they hold in the active company.</param>
/// <param name="tenantContext">Which company they are asking in.</param>
/// <param name="moduleCatalog">Decides whether the tenant has licensed the owning module.</param>
/// <param name="messages">Renders the refusal.</param>
public sealed class PermissionBehavior<TRequest, TResponse>(
    IUserContext userContext,
    ITenantContext tenantContext,
    IModuleCatalog moduleCatalog,
    IMessageCatalog messages) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly RequiresPermissionAttribute[] Required = ReadRequirements();

    /// <inheritdoc />
    /// <remarks>Runs before everything else, so a refusal costs nothing.</remarks>
    public int Order => -1000;

    /// <inheritdoc />
    public Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (Required.Length == 0)
        {
            return next();
        }

        // The installation owner passes every check, and every action they take is audited.
        if (userContext.IsSuperUser)
        {
            return next();
        }

        foreach (var requirement in Required)
        {
            // Licence before permission. Telling someone they lack a permission for a module the
            // organisation never bought sends them to an administrator who cannot help.
            if (!moduleCatalog.IsAvailable(requirement.Module, tenantContext.TenantId))
            {
                throw new AsapMessageException(messages.Render(
                    PlatformMessages.ModuleNotLicensed,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Module"] = requirement.Module,
                    }));
            }

            if (!userContext.Has(requirement.Key))
            {
                throw new AsapMessageException(messages.Render(
                    PlatformMessages.PermissionDenied,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        // The request name is the closest thing to a human description of the
                        // operation that is available without another registry to keep in step.
                        ["Operation"] = DescribeOperation(),
                        ["Permission"] = requirement.Key,
                        ["Company"] = tenantContext.CompanyId?.ToString() ?? "the current company",
                    }));
            }
        }

        return next();
    }

    /// <summary>
    /// Turns a request type name into something a user can read: <c>PostGeneralJournalCommand</c>
    /// becomes "Post general journal".
    /// </summary>
    private static string DescribeOperation()
    {
        var name = typeof(TRequest).Name;

        foreach (var suffix in (string[])["Command", "Query", "Request"])
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var spaced = string.Concat(name.Select(static (c, i) =>
            i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

        return spaced.Length == 0 ? typeof(TRequest).Name : spaced;
    }

    private static RequiresPermissionAttribute[] ReadRequirements()
        => [.. typeof(TRequest).GetCustomAttributes<RequiresPermissionAttribute>(inherit: true)];
}

/// <summary>
/// Reports which requests are guarded and which are open, so an unguarded command cannot go
/// unnoticed.
/// </summary>
/// <remarks>
/// The weakness of declarative permissions is that forgetting the attribute leaves an operation
/// open, and nothing complains. This closes that gap: the host runs the audit at startup, logs
/// anything unguarded, and an administrator can read the same report from the API.
/// </remarks>
public static class PermissionAudit
{
    private static readonly ConcurrentDictionary<Type, RequestPermissionReport> Cache = new();

    /// <summary>What one request declares about the permission it needs.</summary>
    /// <param name="RequestType">The request.</param>
    /// <param name="RequiredPermissions">Permission keys it demands.</param>
    /// <param name="DeliberatelyOpenReason">
    /// Why it deliberately needs none, from <see cref="NoPermissionRequiredAttribute"/>.
    /// </param>
    public sealed record RequestPermissionReport(
        Type RequestType,
        IReadOnlyList<string> RequiredPermissions,
        string? DeliberatelyOpenReason)
    {
        /// <summary>
        /// True when the request declares no permission and gives no reason. Every one of these
        /// is either a deliberate omission that should say so, or a hole.
        /// </summary>
        public bool IsUndeclared => RequiredPermissions.Count == 0 && DeliberatelyOpenReason is null;
    }

    /// <summary>Reads what a request declares.</summary>
    /// <param name="requestType">The request type.</param>
    public static RequestPermissionReport Describe(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return Cache.GetOrAdd(requestType, static type => new RequestPermissionReport(
            type,
            [.. type.GetCustomAttributes<RequiresPermissionAttribute>(inherit: true).Select(static a => a.Key)],
            type.GetCustomAttribute<NoPermissionRequiredAttribute>(inherit: true)?.Reason));
    }

    /// <summary>
    /// Audits every request type in a set of assemblies.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan, normally the platform plus every module.</param>
    /// <returns>One report per request, ordered by name so the output is stable between runs.</returns>
    public static IReadOnlyList<RequestPermissionReport> AuditAll(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return
        [
            .. assemblies
                .SelectMany(SafeGetTypes)
                .Where(static t => t is { IsAbstract: false, IsInterface: false }
                                   && Array.Exists(
                                       t.GetInterfaces(),
                                       static i => i.IsGenericType
                                                   && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
                .Select(Describe)
                .OrderBy(static r => r.RequestType.FullName, StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A module with an unresolvable dependency should not stop the audit reporting on
            // everything else; the loader has already reported that module separately.
            return ex.Types.Where(static t => t is not null)!;
        }
    }
}
