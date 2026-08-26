using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Modules.Finance.Ledger;

/// <summary>
/// The last transaction number issued in a company. One row per company.
/// </summary>
/// <remarks>
/// <para>
/// A transaction number groups every entry written by one posting run: all the entries of a sales
/// invoice share one, which is what makes "show me the whole transaction" a single indexed query.
/// </para>
/// <para>
/// Allocated once per posting rather than once per entry, which matters at till volume. A counter
/// per entry would serialise every line of every receipt on one row; per posting, the lock is
/// held for a single statement and released.
/// </para>
/// <para>
/// A counter row rather than a database sequence because sequences are per database, and ASAP
/// puts many companies in one. Numbering restarts at 1 for each company, which is what an
/// accountant expects when they look at their own books.
/// </para>
/// </remarks>
public sealed class TransactionCounter : Entity, ICompanyScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public Guid CompanyId { get; set; }

    /// <summary>The last number issued. The next posting takes this plus one.</summary>
    public long LastTransactionNo { get; set; }
}
