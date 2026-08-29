using ASAP.Platform.Kernel.Results;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// Puts what a rate movement left on a control account somewhere it belongs.
/// </summary>
/// <remarks>
/// <para>
/// A seam of exactly one method, because settling a customer's account and posting a journal are
/// two different jobs and only one of them has anything to do with the general ledger. Applying a
/// payment to an invoice normally writes nothing at all — see
/// <see cref="PartyApplicationService"/> — and the whole posting stack is a heavy dependency for
/// the exception rather than the rule.
/// </para>
/// <para>
/// It is also what keeps the application rules testable. The arithmetic of settling two entries
/// raised at different rates is worth exercising on its own, and a test of it should not have to
/// stand up a chart of accounts and a fiscal calendar to find out what number comes out.
/// </para>
/// </remarks>
public interface IExchangeDifferencePoster
{
    /// <summary>
    /// Posts the difference between what two settled entries took off the control account.
    /// </summary>
    /// <param name="controlAccountNo">The control account left holding it.</param>
    /// <param name="difference">
    /// The residual, signed the way the control account is short: positive is a loss to the
    /// company, negative a gain.
    /// </param>
    /// <param name="currencyCode">What the two entries were written in, for the description.</param>
    /// <param name="documentNo">The document being settled, so the entry can be traced to it.</param>
    /// <param name="branchId">Where the document was raised, or null for head office.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The transaction it was posted under, or why it could not be.</returns>
    Task<Result<long>> PostAsync(
        string controlAccountNo,
        decimal difference,
        string? currencyCode,
        string? documentNo,
        Guid? branchId,
        CancellationToken cancellationToken = default);
}
