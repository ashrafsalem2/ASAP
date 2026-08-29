using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;

namespace ASAP.Modules.Finance.Journals;

/// <summary>One line of a journal being posted through the API.</summary>
/// <param name="AccountNo">
/// What the line posts to. A general ledger account number such as <c>6400</c>, or a customer or
/// vendor number when <paramref name="AccountType"/> says so.
/// </param>
/// <param name="Amount">The signed amount. Positive debits the account, negative credits it.</param>
/// <param name="Description">What the entry should say. Falls back to the account name.</param>
/// <param name="BalancingAccountNo">
/// What this line balances against. When given, the line stands alone and produces two entries.
/// </param>
/// <param name="PostingDate">The date to report the entry in. Defaults to today.</param>
/// <param name="AccountType">
/// Whether the line posts to a general ledger account or to a customer or vendor. Modelled on the
/// line rather than the journal so one batch can hold an invoice and its contra, which is how
/// anybody actually keys a purchase day book.
/// </param>
/// <param name="ExternalDocumentNo">
/// The other side's reference, such as the number printed on the vendor's own invoice. Carried on
/// the party entry, where it is the thing people search by when a supplier telephones.
/// </param>
/// <param name="TaxCode">
/// The tax to apply, or null for a line carrying none. ASAP works out the tax and posts it beside
/// the line, so the person keying it never has to.
/// </param>
/// <param name="TaxIncludedInAmount">
/// Whether <paramref name="Amount"/> already contains the tax. False for a net figure the tax is
/// added to; true for a shelf price the tax comes out of.
/// </param>
/// <param name="BranchId">
/// Which branch the entry belongs to, or null for the branch the caller is signed in to. Stated
/// per line because one document can belong to several -- a payroll run splits a month's wage
/// between the branches somebody actually worked at.
/// </param>
/// <param name="CurrencyCode">
/// What the amount is written in, or null for the company's own currency. When it is set,
/// <paramref name="Amount"/> is read as being in that currency and is converted at the rate in
/// force on the line's posting date -- so a line saying 1,000 USD says 1,000, not what 1,000
/// happened to be worth this morning.
/// </param>
public sealed record PostJournalLine(
    string AccountNo,
    decimal Amount,
    string? Description = null,
    string? BalancingAccountNo = null,
    DateOnly? PostingDate = null,
    JournalAccountType AccountType = JournalAccountType.GlAccount,
    string? ExternalDocumentNo = null,
    string? TaxCode = null,
    bool TaxIncludedInAmount = false,
    Guid? BranchId = null,
    string? CurrencyCode = null);

/// <summary>
/// Posts a set of journal lines to the general ledger.
/// </summary>
/// <remarks>
/// Guarded by <c>Finance.Journal.Post</c>, which is deliberately distinct from the permission to
/// prepare a journal: the clerk who keys one is usually not the person who commits it.
/// </remarks>
/// <param name="BatchCode">The batch being posted, used in messages.</param>
/// <param name="Lines">The lines to post.</param>
/// <param name="DocumentNo">The document number the entries carry.</param>
/// <param name="Description">Default description for lines that supply none.</param>
/// <param name="OverrideReason">
/// Why the user is pushing past a block. Recorded in the audit log alongside the code overridden.
/// </param>
[RequiresPermission("Finance", "Journal", PermissionAction.Post)]
public sealed record PostJournalCommand(
    string BatchCode,
    IReadOnlyList<PostJournalLine> Lines,
    string? DocumentNo = null,
    string? Description = null,
    string? OverrideReason = null) : ICommand<PostingReceipt>;

/// <summary>
/// Posts a hand-keyed journal, by way of the shared document poster.
/// </summary>
/// <remarks>
/// The one thing this adds over any other caller of <see cref="DocumentPostingService"/> is the
/// word manual, and that word is the whole point: it is what turns on the protection that keeps
/// a person from keying an entry straight into a control account.
/// </remarks>
public sealed class PostJournalCommandHandler(DocumentPostingService documents)
    : IRequestHandler<PostJournalCommand, Result<PostingReceipt>>
{
    /// <inheritdoc />
    public Task<Result<PostingReceipt>> HandleAsync(
        PostJournalCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return documents.PostAsync(
            new DocumentPosting(
                BatchCode: request.BatchCode,
                Lines: request.Lines,
                SourceCode: "GENJNL",
                IsManualEntry: true,
                DocumentType: GlDocumentType.None,
                DocumentNo: request.DocumentNo,
                Description: request.Description,
                OverrideReason: request.OverrideReason),
            cancellationToken);
    }
}
