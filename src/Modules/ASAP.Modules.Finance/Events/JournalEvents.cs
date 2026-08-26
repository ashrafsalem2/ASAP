using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Kernel.Events;

namespace ASAP.Modules.Finance.Events;

/// <summary>
/// Raised after ASAP has checked a journal and before a single entry is written.
/// </summary>
/// <remarks>
/// <para>
/// The main extension point in Finance. A subscriber sees the lines exactly as they are about to
/// post and may object, which stops the posting and shows the user the objection as an ordinary
/// ASAP message. Rules the core knows nothing about live here: a spending limit per department, a
/// requirement that capital expenditure carries a project, a client's own approval threshold.
/// </para>
/// <para>
/// Raised after the built-in validation, deliberately. A subscriber can then assume the batch
/// already balances and every account is postable, and concentrate on its own rule instead of
/// re-checking what ASAP has already checked.
/// </para>
/// </remarks>
public sealed class JournalPosting : VetoableEvent
{
    /// <inheritdoc />
    public override string EventName => "Finance.JournalPosting";

    /// <summary>The batch being posted.</summary>
    public required string BatchCode { get; init; }

    /// <summary>The lines, with their accounts already resolved.</summary>
    public required IReadOnlyList<PostingLineView> Lines { get; init; }

    /// <summary>The document number the entries will carry.</summary>
    public string? DocumentNo { get; init; }

    /// <summary>The date the entries will be reported in.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>The total of the debit side, for a subscriber testing a threshold.</summary>
    public decimal TotalDebit { get; init; }
}

/// <summary>
/// Raised once entries have been written and the transaction has committed.
/// </summary>
/// <remarks>
/// Delivered through the outbox, so a subscriber that fails cannot undo a posting that has
/// already happened. This is the hook for anything crossing a boundary: notifying head office,
/// pushing to a reporting store, sending a statement.
/// </remarks>
public sealed class JournalPosted : IIntegrationEvent
{
    /// <inheritdoc />
    public string EventName => "Finance.JournalPosted";

    /// <inheritdoc />
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>The transaction number grouping every entry this posting wrote.</summary>
    public long TransactionNo { get; init; }

    /// <summary>The document number the entries carry.</summary>
    public string? DocumentNo { get; init; }

    /// <summary>The date the entries are reported in.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>How many entries were written.</summary>
    public int EntryCount { get; init; }

    /// <summary>The total of the debit side, which equals the credit side.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Where the posting came from, for example <c>GENJNL</c>.</summary>
    public required string SourceCode { get; init; }
}
