using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Kernel.Entities;

/// <summary>
/// An entity that records who created and last changed it. The persistence layer fills the
/// stamps in on save, so no handler has to remember to.
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }
}

/// <summary>
/// Master data owned by one company: accounts, items, customers, employees.
/// </summary>
/// <remarks>
/// Rows are stamped with the active company on insert and filtered to it on every read, so a
/// module cannot see another company data even by writing a careless query. Master data is
/// soft-deleted rather than removed, because posted history keeps pointing at it long after
/// someone stops using it: an item withdrawn from sale must still resolve on last year invoices.
/// </remarks>
public abstract class CompanyEntity : AuditableEntity, ICompanyScoped, ISoftDeletable, IConcurrencyAware
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public Guid CompanyId { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Data that originates at a particular branch, such as a till session or a stock transfer.
/// </summary>
public abstract class BranchEntity : CompanyEntity, IBranchScoped
{
    /// <inheritdoc />
    public Guid? BranchId { get; set; }
}

/// <summary>
/// A posted ledger entry: a general ledger entry, an item ledger entry, a value entry.
/// </summary>
/// <remarks>
/// Ledger entries are deliberately neither soft-deletable nor concurrency-stamped. Once posted,
/// an entry is a historical fact and is never edited or removed; a mistake is corrected by
/// posting a reversal, which leaves both the error and the correction visible in the audit
/// trail. Leaving the deletion fields off the type means no code can quietly delete one.
/// </remarks>
public abstract class LedgerEntity : Entity, ICompanyScoped, IAuditable
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public Guid CompanyId { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }
}
