using ASAP.Platform.Kernel.Security;

namespace ASAP.Platform.Kernel.Cqrs;

/// <summary>
/// States which permission a request needs. The pipeline enforces it before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// Putting the requirement on the request rather than inside the handler is what makes the ASAP
/// permission model auditable. Every guarded operation declares its requirement in one visible
/// place, a startup check can list every request and the permission it needs, and no handler
/// can quietly forget to check.
/// </para>
/// <para>
/// A request carrying no attribute is treated as needing no permission beyond being signed in,
/// which is correct for things like reading the current menu. The startup check reports every
/// such request so an unguarded command cannot slip through unnoticed.
/// </para>
/// </remarks>
/// <param name="module">Owning module, for example <c>Finance</c>.</param>
/// <param name="resource">Guarded resource, for example <c>Journal</c>.</param>
/// <param name="action">Verb being attempted.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPermissionAttribute(string module, string resource, PermissionAction action)
    : Attribute
{
    /// <summary>Owning module.</summary>
    public string Module { get; } = module;

    /// <summary>Guarded resource.</summary>
    public string Resource { get; } = resource;

    /// <summary>Verb being attempted.</summary>
    public PermissionAction Action { get; } = action;

    /// <summary>The permission key this attribute resolves to.</summary>
    public string Key => PermissionDescriptor.BuildKey(Module, Resource, Action);
}

/// <summary>
/// Marks a request as deliberately needing no permission beyond authentication, so the startup
/// check can tell an intentional omission from a forgotten one.
/// </summary>
/// <param name="reason">Why this operation needs no permission. Recorded in the audit report.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class NoPermissionRequiredAttribute(string reason) : Attribute
{
    /// <summary>Why this operation is open to any signed-in user.</summary>
    public string Reason { get; } = reason;
}
