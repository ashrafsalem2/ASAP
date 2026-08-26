namespace ASAP.Platform.Kernel.Tenancy;

/// <summary>
/// Data belonging to one ASAP subscriber. A tenant is the top of the isolation hierarchy:
/// tenant -> company -> branch. Rows carrying this interface are filtered to the caller's
/// tenant by a global query filter that cannot be bypassed from module code.
/// </summary>
public interface ITenantScoped
{
    /// <summary>Owning tenant. Assigned on insert from the ambient tenant context.</summary>
    Guid TenantId { get; set; }
}
