using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Kernel.Documents;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Sales.Reporting;

/// <summary>Says which customer a sales order was with.</summary>
/// <param name="context">The unit of work.</param>
public sealed class SalesDocumentParties(AsapDbContext context) : IDocumentParties
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentParty>> ForAsync(
        IReadOnlyCollection<string> documentNos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentNos);

        if (documentNos.Count == 0)
        {
            return [];
        }

        var wanted = documentNos.ToList();

        return await context.Set<SalesOrder>()
            .AsNoTracking()
            .Where(o => wanted.Contains(o.No))
            .Select(static o => new DocumentParty(o.No, o.CustomerNo, o.CustomerName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
