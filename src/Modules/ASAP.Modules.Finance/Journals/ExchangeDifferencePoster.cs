using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;

namespace ASAP.Modules.Finance.Journals;

/// <summary>
/// Posts an exchange difference through the ordinary document poster.
/// </summary>
/// <remarks>
/// Two lines and no party. The subsidiary ledger is already right — both entries were settled by
/// what was actually paid — so what is wanted is a general ledger entry that takes the orphaned
/// amount off the control account and calls it what it is. A party line would write a third
/// subsidiary entry and reopen the very balance this is closing.
/// </remarks>
/// <param name="documents">Posts the pair.</param>
/// <param name="setup">Supplies the two accounts a difference can land on.</param>
/// <param name="messages">Renders the refusal when neither is set up.</param>
/// <param name="clock">Supplies the day the difference was realised.</param>
public sealed class ExchangeDifferencePoster(
    DocumentPostingService documents,
    ISetupService setup,
    IMessageCatalog messages,
    IClock clock) : IExchangeDifferencePoster
{
    /// <inheritdoc />
    public async Task<Result<long>> PostAsync(
        string controlAccountNo,
        decimal difference,
        string? currencyCode,
        string? documentNo,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        // Positive means the control account is left short, so the company is worse off by it.
        var settingKey = difference > 0m
            ? $"{FinanceModule.Id}.Currency.LossAccount"
            : $"{FinanceModule.Id}.Currency.GainAccount";

        var account = await setup
            .GetAsync<string>(settingKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(account))
        {
            return Result<long>.Failure(messages.Render(
                FinanceMessages.NoExchangeDifferenceAccount,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SettingKey"] = settingKey,
                    ["Amount"] = Math.Abs(difference),
                }));
        }

        var description = documentNo is { Length: > 0 }
            ? $"{documentNo} — exchange difference {currencyCode}"
            : $"Exchange difference {currencyCode}";

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: documentNo ?? "FXDIFF",
                    Lines:
                    [
                        new PostJournalLine(controlAccountNo, -difference, description),
                        new PostJournalLine(account, difference, description),
                    ],
                    SourceCode: "FXDIFF",

                    // Nobody keyed this. The control account refuses hand-keyed entries, and this
                    // is precisely the sort of writing that restriction leaves room for.
                    IsManualEntry: false,
                    DocumentType: GlDocumentType.None,
                    DocumentNo: documentNo,
                    Description: description,

                    // The branch that raised the document, so a difference on a Jeddah invoice is
                    // Jeddah's rather than head office's.
                    BranchId: branchId,

                    // Today, not the document's date. The difference did not exist until the two
                    // were settled against each other, and dating it back would restate a month
                    // that has very possibly been reported already.
                    PostingDate: clock.Today),
                cancellationToken)
            .ConfigureAwait(false);

        return posted.Failed
            ? Result<long>.FailureFrom(posted)
            : Result<long>.Success(posted.Value.TransactionNo, posted.Messages);
    }
}
