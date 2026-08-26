using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Finance;

/// <summary>
/// Everything the Finance module can tell a user, declared once so each is translated,
/// documented, and obeys the rule that a block must offer a way forward.
/// </summary>
public static class FinanceMessages
{
    /// <summary>Debits and credits do not agree.</summary>
    public static readonly MessageCode OutOfBalance = new("FIN.JOURNAL.OUT_OF_BALANCE");

    /// <summary>There is nothing in the batch to post.</summary>
    public static readonly MessageCode BatchEmpty = new("FIN.JOURNAL.BATCH_EMPTY");

    /// <summary>A line names no account.</summary>
    public static readonly MessageCode AccountMissing = new("FIN.JOURNAL.ACCOUNT_MISSING");

    /// <summary>A line names an account that is a heading or a total.</summary>
    public static readonly MessageCode AccountNotPostable = new("FIN.ACCOUNT.NOT_POSTABLE");

    /// <summary>A line names an account withdrawn from use.</summary>
    public static readonly MessageCode AccountBlocked = new("FIN.ACCOUNT.BLOCKED");

    /// <summary>A person is posting by hand to an account only a module should touch.</summary>
    public static readonly MessageCode DirectPostingNotAllowed = new("FIN.ACCOUNT.DIRECT_POSTING_BLOCKED");

    /// <summary>A line carries no amount.</summary>
    public static readonly MessageCode AmountZero = new("FIN.JOURNAL.AMOUNT_ZERO");

    /// <summary>The posting date falls in no defined period.</summary>
    public static readonly MessageCode NoOpenPeriod = new("FIN.PERIOD.NOT_DEFINED");

    /// <summary>The period covering the posting date is closed.</summary>
    public static readonly MessageCode PeriodClosed = new("FIN.PERIOD.CLOSED");

    /// <summary>The financial year covering the posting date is closed.</summary>
    public static readonly MessageCode YearClosed = new("FIN.YEAR.CLOSED");

    /// <summary>The posting date falls outside the window this user may post in.</summary>
    public static readonly MessageCode OutsidePostingWindow = new("FIN.PERIOD.OUTSIDE_POSTING_WINDOW");

    /// <summary>An entry has already been reversed once.</summary>
    public static readonly MessageCode AlreadyReversed = new("FIN.ENTRY.ALREADY_REVERSED");

    /// <summary>Something tried to change a posted entry.</summary>
    public static readonly MessageCode EntryImmutable = new("FIN.ENTRY.IMMUTABLE");

    /// <summary>The posting succeeded.</summary>
    public static readonly MessageCode Posted = new("FIN.JOURNAL.POSTED");

    /// <summary>Every message the module declares.</summary>
    public static IReadOnlyCollection<MessageDefinition> All { get; } =
    [
        new()
        {
            Code = OutOfBalance,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("The journal is out of balance", "القيد غير متوازن"),
            Detail = new LocalizedText(
                "Debits total {Debit:N2} and credits total {Credit:N2}, a difference of "
                + "{Difference:N2} {Currency}.",
                "إجمالي المدين {Debit:N2} وإجمالي الدائن {Credit:N2}، بفارق {Difference:N2} {Currency}."),
            Resolution = new LocalizedText(
                "Add a line for {Difference:N2} {Currency}, or set a balancing account on the "
                + "lines that have none.",
                "أضف سطرًا بقيمة {Difference:N2} {Currency}، أو حدد حسابًا مقابلًا للسطور التي لا تحتوي على واحد."),
            HelpTopic = "finance/general-journals",
        },
        new()
        {
            Code = BatchEmpty,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("There is nothing to post", "لا يوجد ما يمكن ترحيله"),
            Detail = new LocalizedText(
                "Batch {Batch} has no lines.",
                "الدفعة {Batch} لا تحتوي على سطور."),
            Resolution = new LocalizedText("Add at least one line before posting.", "أضف سطرًا واحدًا على الأقل قبل الترحيل."),
        },
        new()
        {
            Code = AccountMissing,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A line has no account", "أحد السطور بدون حساب"),
            Detail = new LocalizedText(
                "Line {LineNo} names no account to post to.",
                "السطر {LineNo} لا يحدد حسابًا للترحيل إليه."),
            Resolution = new LocalizedText("Choose an account on line {LineNo}.", "اختر حسابًا في السطر {LineNo}."),
        },
        new()
        {
            Code = AccountNotPostable,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That account cannot take entries", "لا يمكن الترحيل إلى هذا الحساب"),
            Detail = new LocalizedText(
                "Account {AccountNo} {AccountName} is a {AccountType}, which is part of the report "
                + "structure rather than a place a balance can rest.",
                "الحساب {AccountNo} {AccountName} من نوع {AccountType}، وهو جزء من هيكل التقرير "
                + "وليس مكانًا يمكن أن يستقر فيه رصيد."),
            Resolution = new LocalizedText(
                "Choose a posting account on line {LineNo}. Headings and totals only shape the chart.",
                "اختر حساب ترحيل في السطر {LineNo}. العناوين والمجاميع تُستخدم لتنسيق الشجرة فقط."),
            HelpTopic = "finance/chart-of-accounts",
        },
        new()
        {
            Code = AccountBlocked,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That account is blocked", "هذا الحساب محظور"),
            Detail = new LocalizedText(
                "Account {AccountNo} {AccountName} has been withdrawn from use.",
                "تم سحب الحساب {AccountNo} {AccountName} من الاستخدام."),
            Resolution = new LocalizedText(
                "Choose a different account, or unblock {AccountNo} in the chart of accounts.",
                "اختر حسابًا آخر، أو ألغِ حظر {AccountNo} في شجرة الحسابات."),
        },
        new()
        {
            Code = DirectPostingNotAllowed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "This account is maintained by the system",
                "هذا الحساب يديره النظام"),
            Detail = new LocalizedText(
                "Account {AccountNo} {AccountName} is written only by the module that owns it. A "
                + "manual entry here would make it disagree with the ledger behind it, and the "
                + "difference is very hard to find afterwards.",
                "الحساب {AccountNo} {AccountName} يُكتب فقط بواسطة الوحدة التي تملكه. القيد اليدوي "
                + "هنا سيجعله مخالفًا لدفتر الأستاذ المرتبط به، والفرق يصعب تتبعه لاحقًا."),
            Resolution = new LocalizedText(
                "Post the underlying document instead. If this genuinely is a correction, use a "
                + "reversal on the original entry.",
                "قم بترحيل المستند الأصلي بدلاً من ذلك. وإذا كان هذا تصحيحًا فعليًا، استخدم عكس القيد الأصلي."),
            OverridePermission = "Finance.Account.Override",
            HelpTopic = "finance/chart-of-accounts",
        },
        new()
        {
            Code = AmountZero,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("A line has no amount", "أحد السطور بدون مبلغ"),
            Detail = new LocalizedText(
                "Line {LineNo} would post nothing.",
                "السطر {LineNo} لن يرحّل أي مبلغ."),
            Resolution = new LocalizedText(
                "Enter an amount on line {LineNo}, or remove the line.",
                "أدخل مبلغًا في السطر {LineNo}، أو احذف السطر."),
        },
        new()
        {
            Code = NoOpenPeriod,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No period covers that date", "لا توجد فترة تغطي هذا التاريخ"),
            Detail = new LocalizedText(
                "Nothing in the fiscal calendar covers {PostingDate:d}.",
                "لا يوجد في التقويم المالي ما يغطي {PostingDate:d}."),
            Resolution = new LocalizedText(
                "Create the financial year covering {PostingDate:d}, or correct the date on line {LineNo}. "
                + "This usually means a new year has begun and has not been set up yet.",
                "أنشئ السنة المالية التي تغطي {PostingDate:d}، أو صحّح التاريخ في السطر {LineNo}. "
                + "يحدث هذا عادة عند بداية سنة جديدة لم يتم إعدادها بعد."),
            HelpTopic = "finance/fiscal-periods",
        },
        new()
        {
            Code = PeriodClosed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That period is closed", "هذه الفترة مغلقة"),
            Detail = new LocalizedText(
                "{PeriodName} was closed, and {PostingDate:d} falls inside it. It has already been reported.",
                "تم إغلاق {PeriodName}، ويقع {PostingDate:d} ضمنها. وقد تم إصدار تقاريرها بالفعل."),
            Resolution = new LocalizedText(
                "Post to the current period instead, or ask someone with permission to reopen {PeriodName}.",
                "رحّل إلى الفترة الحالية بدلاً من ذلك، أو اطلب من صاحب صلاحية إعادة فتح {PeriodName}."),
            OverridePermission = "Finance.Period.Override",
            HelpTopic = "finance/fiscal-periods",
        },
        new()
        {
            Code = YearClosed,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That financial year is closed", "السنة المالية مغلقة"),
            Detail = new LocalizedText(
                "Year {FiscalYear} is closed, and {PostingDate:d} falls inside it. The statements "
                + "for that year have been issued.",
                "السنة {FiscalYear} مغلقة، ويقع {PostingDate:d} ضمنها. وقد تم إصدار قوائمها المالية."),

            // No override permission. A closed year is refused to everyone, including the
            // installation owner: the accounts have been filed, and a late entry would make the
            // filed figures wrong with nothing to show it happened.
            Resolution = new LocalizedText(
                "Post the correction to the current year. An adjustment to a closed year is a "
                + "prior-period adjustment and belongs in this year's books.",
                "رحّل التصحيح إلى السنة الحالية. التعديل على سنة مغلقة يُعد تعديل فترة سابقة "
                + "ومكانه دفاتر السنة الحالية."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = OutsidePostingWindow,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That date is outside your posting window", "التاريخ خارج نطاق الترحيل المسموح لك"),
            Detail = new LocalizedText(
                "You may post between {WindowFrom:d} and {WindowTo:d}, and line {LineNo} is dated {PostingDate:d}.",
                "يمكنك الترحيل بين {WindowFrom:d} و{WindowTo:d}، والسطر {LineNo} مؤرخ في {PostingDate:d}."),
            Resolution = new LocalizedText(
                "Correct the date on line {LineNo}, or ask an administrator to widen your posting window.",
                "صحّح التاريخ في السطر {LineNo}، أو اطلب من المسؤول توسيع نطاق الترحيل المسموح لك."),
            OverridePermission = "Finance.Period.Override",
            HelpTopic = "finance/posting-window",
        },
        new()
        {
            Code = AlreadyReversed,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That entry has already been reversed", "تم عكس هذا القيد بالفعل"),
            Detail = new LocalizedText(
                "Transaction {TransactionNo} was reversed on {ReversedOn:d}.",
                "تم عكس الحركة {TransactionNo} بتاريخ {ReversedOn:d}."),
            Resolution = new LocalizedText(
                "Nothing further is needed. If the reversal was itself wrong, post a new entry.",
                "لا حاجة لأي إجراء إضافي. وإذا كان العكس نفسه خاطئًا، رحّل قيدًا جديدًا."),
        },
        new()
        {
            Code = EntryImmutable,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("A posted entry cannot be changed", "لا يمكن تعديل قيد مرحّل"),
            Detail = new LocalizedText(
                "Entry {TransactionNo} is posted. ASAP never edits or deletes a ledger entry.",
                "القيد {TransactionNo} مرحّل. لا يقوم ASAP بتعديل أو حذف قيود دفتر الأستاذ أبدًا."),
            Resolution = new LocalizedText(
                "Reverse it and post the correct entry. Both stay visible, which is what makes the "
                + "trail worth having.",
                "قم بعكسه ثم رحّل القيد الصحيح. يبقى كلاهما ظاهرًا، وهذا ما يجعل سجل التدقيق ذا قيمة."),
            HelpTopic = "finance/reversals",
        },
        new()
        {
            Code = Posted,
            Severity = MessageSeverity.Success,
            Title = new LocalizedText("Posted", "تم الترحيل"),
            Detail = new LocalizedText(
                "{EntryCount} entries posted as transaction {TransactionNo}, document {DocumentNo}.",
                "تم ترحيل {EntryCount} قيدًا ضمن الحركة {TransactionNo}، المستند {DocumentNo}."),
        },
    ];
}
