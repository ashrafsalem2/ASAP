using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Journals;

/// <summary>What happens to a recurring line's amount once it has been posted.</summary>
public enum RecurringMethod
{
    /// <summary>
    /// The amount stays. Rent at a fixed monthly figure: post it, and next month post it again.
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// The amount is cleared after posting, so somebody has to enter it each time.
    /// </summary>
    /// <remarks>
    /// For a cost that recurs reliably and varies every time — a utility bill. Clearing it is the
    /// point: a variable line left at last month's figure posts last month's figure, and nothing
    /// about the result looks wrong.
    /// </remarks>
    Variable = 1,

    /// <summary>
    /// Posts whatever the account's balance is and clears it to nothing.
    /// </summary>
    /// <remarks>
    /// For a holding account that should be empty at each month end — a suspense account, or a
    /// clearing account between two systems. The line does not name an amount because the ledger
    /// already knows it.
    /// </remarks>
    Balance = 2,

    /// <summary>
    /// Posts the amount, and posts the opposite of it on the following day.
    /// </summary>
    /// <remarks>
    /// The accrual, and the reason recurring journals earn their place. A cost belonging to March
    /// whose invoice arrives in April is accrued on the thirty-first and reversed on the first, so
    /// March is right and April is not double-counted when the invoice lands. Doing that by hand
    /// is two journals a month per accrual, and the second one is the one that gets forgotten.
    /// </remarks>
    ReversingFixed = 3,

    /// <summary>Reversing, and cleared after posting like <see cref="Variable"/>.</summary>
    ReversingVariable = 4,
}

/// <summary>
/// A journal that is posted again and again on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// Depreciation, rent, insurance spread over a year, an accrual for a cost whose invoice has not
/// arrived. Each is the same handful of lines every month, and keying them by hand is a job that
/// is done twelve times and forgotten once — which is the month somebody finds out about.
/// </para>
/// <para>
/// A batch is a template rather than a document. Posting it produces an ordinary journal that
/// goes through the same posting engine as everything else, and then the batch moves its own
/// dates on. Nothing about the entries it produces is special, which is what stops recurring
/// journals becoming a second ledger with its own rules.
/// </para>
/// </remarks>
public sealed class RecurringJournalBatch : CompanyEntity
{
    /// <summary>Short stable code, for example <c>MONTH-END</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What it is for.</summary>
    public string? Description { get; set; }

    /// <summary>Whether it may still be posted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The lines.</summary>
    public ICollection<RecurringJournalLine> Lines { get; set; } = [];

    /// <summary>
    /// The earliest day any line is next due, or null when nothing is due.
    /// </summary>
    /// <remarks>
    /// What a "what is due" list sorts on. Lines within a batch can fall due on different days —
    /// a quarterly line beside eleven monthly ones — and the batch is due when the first of them
    /// is.
    /// </remarks>
    public DateOnly? NextDue
        => Lines.Where(static l => l.NextPostingDate is not null)
                .Select(static l => l.NextPostingDate!.Value)
                .DefaultIfEmpty()
                .Min() is var earliest && earliest == default
            ? null
            : earliest;
}

/// <summary>One line of a recurring journal.</summary>
public sealed class RecurringJournalLine : CompanyEntity
{
    /// <summary>The batch this belongs to.</summary>
    public Guid RecurringJournalBatchId { get; set; }

    /// <summary>Navigation to the batch.</summary>
    public RecurringJournalBatch? RecurringJournalBatch { get; set; }

    /// <summary>Where it sits in the batch.</summary>
    public int Order { get; set; }

    /// <summary>The account it posts to.</summary>
    public required string AccountNo { get; set; }

    /// <summary>The account the other side goes to, when the line balances itself.</summary>
    public string? BalancingAccountNo { get; set; }

    /// <summary>What the entry says.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// The amount, on the ledger's convention. Ignored for <see cref="RecurringMethod.Balance"/>.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>What happens to the amount once it has been posted.</summary>
    public RecurringMethod Method { get; set; } = RecurringMethod.Fixed;

    /// <summary>
    /// How far forward each posting moves the next one, as a date formula such as <c>1M+CM</c>.
    /// </summary>
    public required string RecurrenceFormula { get; set; }

    /// <summary>The next day this line is due, or null when it has finished.</summary>
    public DateOnly? NextPostingDate { get; set; }

    /// <summary>The day after which it stops, or null to run forever.</summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>The branch to charge, or null for the company.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// How the line is analysed, as dimension code to value code, stored as it was written.
    /// </summary>
    /// <remarks>
    /// Held as text rather than as a resolved set. A recurring line outlives the values it names —
    /// a department is retired, a project ends — and a stored set would keep posting to it in
    /// silence. Resolved at each posting instead, so a value that has gone refuses the posting
    /// and says so.
    /// </remarks>
    public string? Dimensions { get; set; }

    /// <summary>Whether the line has anything left to do.</summary>
    public bool IsDue(DateOnly on)
        => NextPostingDate is { } due && due <= on && (ExpiresOn is null || due <= ExpiresOn);

    /// <summary>Whether posting it also posts the opposite the next day.</summary>
    public bool Reverses
        => Method is RecurringMethod.ReversingFixed or RecurringMethod.ReversingVariable;

    /// <summary>Whether the amount is cleared once it has been posted.</summary>
    public bool ClearsAmount
        => Method is RecurringMethod.Variable or RecurringMethod.ReversingVariable;
}
