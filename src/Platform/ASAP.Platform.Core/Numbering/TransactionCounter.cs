using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Numbering;

/// <summary>
/// The last transaction number issued in a company. One row per company.
/// </summary>
/// <remarks>
/// A counter row rather than a database sequence, because sequences are per database and ASAP puts
/// many companies in one. Numbering restarts at 1 for each company, which is what an accountant
/// expects when they look at their own books.
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
