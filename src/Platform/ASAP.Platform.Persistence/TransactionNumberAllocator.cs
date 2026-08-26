using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>Issues transaction numbers from the company counter.</summary>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company being posted in.</param>
public sealed class TransactionNumberAllocator(AsapDbContext context, ITenantContext tenantContext)
    : ITransactionNumberAllocator
{
    /// <inheritdoc />
    public async Task<long> NextAsync(CancellationToken cancellationToken = default)
    {
        var companyId = tenantContext.RequireCompanyId();

        // One statement that increments and returns, so two callers cannot both read the same
        // last number and both claim the next one.
        var allocated = await context.Database
            .SqlQuery<long>(
                $@"UPDATE asap.TransactionCounters
                   SET LastTransactionNo = LastTransactionNo + 1
                   OUTPUT inserted.LastTransactionNo AS Value
                   WHERE CompanyId = {companyId}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (allocated.Count > 0)
        {
            return allocated[0];
        }

        // First posting in this company. Created here rather than at company setup, so the counter
        // stays an implementation detail of posting instead of something company creation has to
        // know about.
        context.TransactionCounters.Add(new TransactionCounter
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = companyId,
            LastTransactionNo = 1,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return 1;
    }
}
