using ASAP.Modules.Pos.Receipts;
using ASAP.Platform.Kernel.Documents;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Reporting;

/// <summary>
/// Says which customer a till receipt was with.
/// </summary>
/// <remarks>
/// Usually the station's walk-in customer, because a cash sale names nobody and the accounting
/// still needs a party. A margin report grouping by customer will find one enormous row there, and
/// nothing is wrong with it: that is genuinely who bought the goods, as far as anybody knows.
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class PosDocumentParties(AsapDbContext context) : IDocumentParties
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

        return await context.Set<PosReceipt>()
            .AsNoTracking()
            .Where(r => wanted.Contains(r.No))
            .Select(static r => new DocumentParty(r.No, r.CustomerNo ?? string.Empty, r.CustomerName ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
