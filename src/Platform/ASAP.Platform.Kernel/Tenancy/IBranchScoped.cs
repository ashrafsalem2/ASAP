namespace ASAP.Platform.Kernel.Tenancy;

/// <summary>
/// Data that originates at one branch — a shop, warehouse or office. Head office rows leave
/// <see cref="BranchId"/> null and are visible to every branch; branch rows are visible to
/// head office and to that branch only.
/// </summary>
public interface IBranchScoped : ICompanyScoped
{
    /// <summary>Originating branch, or null for head-office data.</summary>
    Guid? BranchId { get; set; }
}
