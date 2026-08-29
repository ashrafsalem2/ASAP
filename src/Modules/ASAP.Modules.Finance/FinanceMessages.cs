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

    /// <summary>No bank statement by that identifier.</summary>
    public static readonly MessageCode BankStatementNotFound = new("FIN.BANK.STATEMENT_NOT_FOUND");

    /// <summary>A statement that has been agreed cannot be worked on.</summary>
    public static readonly MessageCode StatementAlreadyReconciled = new("FIN.BANK.ALREADY_RECONCILED");

    /// <summary>The statement's own lines do not add up to the movement it claims.</summary>
    public static readonly MessageCode StatementLinesDoNotAddUp = new("FIN.BANK.LINES_DO_NOT_ADD_UP");

    /// <summary>Some statement lines have nothing in the books behind them.</summary>
    public static readonly MessageCode StatementLinesUnmatched = new("FIN.BANK.LINES_UNMATCHED");

    /// <summary>The books and the bank disagree by more than the outstanding items explain.</summary>
    public static readonly MessageCode ReconciliationDoesNotBalance = new("FIN.BANK.DOES_NOT_BALANCE");

    /// <summary>No ledger entry by that identifier.</summary>
    public static readonly MessageCode BankEntryNotFound = new("FIN.BANK.ENTRY_NOT_FOUND");

    /// <summary>The entry offered is not on the bank's own ledger account.</summary>
    public static readonly MessageCode BankEntryOnAnotherAccount = new("FIN.BANK.ENTRY_WRONG_ACCOUNT");

    /// <summary>Another statement line already claims that entry.</summary>
    public static readonly MessageCode BankEntryAlreadyMatched = new("FIN.BANK.ENTRY_ALREADY_MATCHED");

    /// <summary>A line has been matched to an entry of a different amount.</summary>
    public static readonly MessageCode BankMatchAmountDiffers = new("FIN.BANK.MATCH_AMOUNT_DIFFERS");

    /// <summary>A line names a currency the company does not have.</summary>
    public static readonly MessageCode CurrencyNotFound = new("FIN.CURRENCY.NOT_FOUND");

    /// <summary>A line names a currency that has been withdrawn from use.</summary>
    public static readonly MessageCode CurrencyBlocked = new("FIN.CURRENCY.BLOCKED");

    /// <summary>No rate has been entered for a currency on the day being posted.</summary>
    public static readonly MessageCode NoExchangeRate = new("FIN.CURRENCY.NO_RATE");

    /// <summary>An entry in the company's own currency was given a currency code.</summary>
    public static readonly MessageCode CurrencyIsBase = new("FIN.CURRENCY.IS_BASE");

    /// <summary>Two entries being settled are in different currencies.</summary>
    public static readonly MessageCode ApplicationDifferentCurrencies =
        new("FIN.APPLICATION.DIFFERENT_CURRENCIES");

    /// <summary>No account is set up for the difference a rate movement leaves behind.</summary>
    public static readonly MessageCode NoExchangeDifferenceAccount =
        new("FIN.SETUP.NO_EXCHANGE_DIFFERENCE_ACCOUNT");

    /// <summary>A line carries no amount.</summary>
    public static readonly MessageCode AmountZero = new("FIN.JOURNAL.AMOUNT_ZERO");

    /// <summary>A line names a customer or vendor withdrawn from use.</summary>
    public static readonly MessageCode PartyBlocked = new("FIN.PARTY.BLOCKED");

    /// <summary>A line names a customer or vendor that does not exist.</summary>
    public static readonly MessageCode PartyNotFound = new("FIN.PARTY.NOT_FOUND");

    /// <summary>Posting would take a customer past the credit they are allowed.</summary>
    public static readonly MessageCode CreditLimitExceeded = new("FIN.CUSTOMER.CREDIT_LIMIT_EXCEEDED");

    /// <summary>An application would settle more than is outstanding.</summary>
    public static readonly MessageCode ApplicationTooLarge = new("FIN.APPLICATION.TOO_LARGE");

    /// <summary>An application names two entries that pull the same way.</summary>
    public static readonly MessageCode ApplicationSameDirection = new("FIN.APPLICATION.SAME_DIRECTION");

    /// <summary>An application names entries belonging to different parties.</summary>
    public static readonly MessageCode ApplicationDifferentParties = new("FIN.APPLICATION.DIFFERENT_PARTIES");

    /// <summary>An application names an entry that is already settled.</summary>
    public static readonly MessageCode ApplicationEntryClosed = new("FIN.APPLICATION.ENTRY_CLOSED");

    /// <summary>An application names an entry that does not exist.</summary>
    public static readonly MessageCode ApplicationEntryNotFound = new("FIN.APPLICATION.ENTRY_NOT_FOUND");

    /// <summary>The posting date falls in no defined period.</summary>
    public static readonly MessageCode NoOpenPeriod = new("FIN.PERIOD.NOT_DEFINED");

    /// <summary>The period covering the posting date is closed.</summary>
    public static readonly MessageCode PeriodClosed = new("FIN.PERIOD.CLOSED");

    /// <summary>The financial year named does not exist.</summary>
    public static readonly MessageCode FiscalYearNotFound = new("FIN.YEAREND.YEAR_NOT_FOUND");

    /// <summary>The year's result has already been moved to retained earnings.</summary>
    public static readonly MessageCode YearAlreadyTransferred = new("FIN.YEAREND.ALREADY_TRANSFERRED");

    /// <summary>The year was locked before its result was moved.</summary>
    public static readonly MessageCode YearLockedBeforeTransfer = new("FIN.YEAREND.LOCKED_BEFORE_TRANSFER");

    /// <summary>An earlier year's result is still sitting in the income statement accounts.</summary>
    public static readonly MessageCode EarlierYearNotTransferred = new("FIN.YEAREND.EARLIER_YEAR_OPEN");

    /// <summary>No account is set up for retained earnings.</summary>
    public static readonly MessageCode NoRetainedEarningsAccount = new("FIN.YEAREND.NO_RETAINED_EARNINGS");

    /// <summary>The year had nothing to transfer.</summary>
    public static readonly MessageCode NothingToTransfer = new("FIN.YEAREND.NOTHING_TO_TRANSFER");

    /// <summary>The financial year covering the posting date is closed.</summary>
    public static readonly MessageCode YearClosed = new("FIN.YEAR.CLOSED");

    /// <summary>The posting date falls outside the window this user may post in.</summary>
    public static readonly MessageCode OutsidePostingWindow = new("FIN.PERIOD.OUTSIDE_POSTING_WINDOW");

    /// <summary>An entry has already been reversed once.</summary>
    public static readonly MessageCode AlreadyReversed = new("FIN.ENTRY.ALREADY_REVERSED");

    /// <summary>Something tried to change a posted entry.</summary>
    public static readonly MessageCode EntryImmutable = new("FIN.ENTRY.IMMUTABLE");

    /// <summary>The posting succeeded, and carried a document number.</summary>
    public static readonly MessageCode Posted = new("FIN.JOURNAL.POSTED");

    /// <summary>The posting succeeded on a journal that carried no document number.</summary>
    public static readonly MessageCode PostedWithoutDocument = new("FIN.JOURNAL.POSTED_NO_DOCUMENT");

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
            Code = BankStatementNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such statement", "لا يوجد كشف بهذا المعرّف"),
            Detail = new LocalizedText(
                "Nothing in this company matches {Statement}.",
                "لا يوجد في هذه الشركة ما يطابق {Statement}."),
            Resolution = new LocalizedText(
                "Open the statement from the bank account it belongs to. It may have been in "
                + "another company.",
                "افتح الكشف من الحساب البنكي الذي يتبعه. وربما كان في شركة أخرى."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = StatementAlreadyReconciled,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This statement has been agreed", "هذا الكشف تمت مطابقته"),
            Detail = new LocalizedText(
                "Statement {Statement} was reconciled, and every later reconciliation has been "
                + "measured against it. Changing a match now would make those wrong without "
                + "anybody being told.",
                "الكشف {Statement} تمت مطابقته، وكل مطابقة لاحقة قيست عليه. وتغيير أي ربط الآن "
                + "سيجعل تلك المطابقات خاطئة دون أن يُخطر بذلك أحد."),
            Resolution = new LocalizedText(
                "Correct it on the current statement instead. A difference found late belongs to "
                + "the period it was found in, which is how it can be explained afterwards.",
                "صحّح الأمر في الكشف الحالي بدلًا من ذلك. فالفرق الذي يُكتشف متأخرًا يخص الفترة "
                + "التي اكتُشف فيها، وبهذا يمكن تفسيره لاحقًا."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = StatementLinesDoNotAddUp,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "The statement does not agree with itself", "الكشف لا يتفق مع نفسه"),
            Detail = new LocalizedText(
                "Statement {Statement} moves from its opening to its closing balance by "
                + "{Movement:N2}, but its lines come to {LineTotal:N2} — a gap of "
                + "{Difference:N2}. The difference is inside the statement, not between the "
                + "statement and the books.",
                "ينتقل الكشف {Statement} من رصيده الافتتاحي إلى الختامي بمقدار {Movement:N2}، "
                + "بينما مجموع سطوره {LineTotal:N2}، بفارق {Difference:N2}. والفرق داخل الكشف "
                + "نفسه لا بينه وبين الدفاتر."),
            Resolution = new LocalizedText(
                "Check the opening and closing balances and that every line was entered. No "
                + "amount of matching will close this, so it is worth settling before starting.",
                "تحقق من الرصيدين الافتتاحي والختامي ومن إدخال كل السطور. فلن تُغلق المطابقة "
                + "مهما رُبط من سطور، ويجدر تسوية ذلك قبل البدء."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = StatementLinesUnmatched,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "Some lines are not accounted for", "بعض السطور غير مفسّرة"),
            Detail = new LocalizedText(
                "{Count} line(s) on statement {Statement} have nothing in the books behind them. "
                + "Everything the bank has charged or credited has to exist in the ledger before "
                + "the two can be said to agree.",
                "يوجد {Count} من سطور الكشف {Statement} لا يقابلها شيء في الدفاتر. وكل ما خصمه "
                + "البنك أو أضافه لا بد أن يكون مقيدًا قبل القول بتطابق الطرفين."),
            Resolution = new LocalizedText(
                "Match each line to its entry, or post the entry it is missing. Bank charges and "
                + "interest are the usual answer: they are real costs nobody keyed, and this is "
                + "the moment they are found.",
                "اربط كل سطر بقيده، أو رحّل القيد الناقص. والمصاريف البنكية والفوائد هي الجواب "
                + "المعتاد: فهي تكاليف حقيقية لم يدخلها أحد، وهذه هي اللحظة التي تُكتشف فيها."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = ReconciliationDoesNotBalance,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "The books and the bank still disagree", "لا يزال بين الدفاتر والبنك اختلاف"),
            Detail = new LocalizedText(
                "The books say {LedgerBalance:N2} and the bank says {ClosingBalance:N2}. "
                + "{OutstandingTotal:N2} of that gap is items the bank has not seen yet, which "
                + "leaves {Difference:N2} that nothing explains.",
                "تقول الدفاتر {LedgerBalance:N2} ويقول البنك {ClosingBalance:N2}. ومن هذا الفارق "
                + "{OutstandingTotal:N2} بنود لم يرها البنك بعد، فيبقى {Difference:N2} بلا تفسير."),
            Resolution = new LocalizedText(
                "The number itself is usually the clue: exactly twice a figure means a sign the "
                + "wrong way round, a multiple of nine means two digits transposed. Check this "
                + "statement's matches first, then the previous one — this check covers every "
                + "reconciliation ever made, so an old mistake surfaces here.",
                "الرقم نفسه هو الدليل عادةً: فضعف مبلغٍ بالضبط يعني إشارة معكوسة، ومضاعف تسعة "
                + "يعني تبديل رقمين. راجع ارتباطات هذا الكشف أولًا ثم الكشف السابق، فهذا الفحص "
                + "يشمل كل مطابقة سابقة، ومن ثم يظهر فيه الخطأ القديم."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = BankEntryNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such ledger entry", "لا يوجد قيد بهذا المعرّف"),
            Detail = new LocalizedText(
                "Nothing in the ledger matches {Entry}.",
                "لا يوجد في دفتر الأستاذ ما يطابق {Entry}."),
            Resolution = new LocalizedText(
                "Choose the entry from the list of what is still outstanding on this account.",
                "اختر القيد من قائمة البنود التي لم تُطابق بعد على هذا الحساب."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = BankEntryOnAnotherAccount,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "That entry is on a different account", "هذا القيد على حساب آخر"),
            Detail = new LocalizedText(
                "{Entry} is on account {AccountNo}, and this statement reconciles "
                + "{BankAccountNo}. Whatever the amounts say, money that moved through another "
                + "account is not what this line was.",
                "القيد {Entry} على الحساب {AccountNo}، وهذا الكشف يطابق الحساب {BankAccountNo}. "
                + "ومهما تطابقت المبالغ، فالمال الذي تحرك عبر حساب آخر ليس هو ما يمثله هذا السطر."),
            Resolution = new LocalizedText(
                "Find the entry on this bank's own account. If there is genuinely none, the "
                + "entry was posted to the wrong account and wants correcting rather than "
                + "matching.",
                "ابحث عن القيد على حساب هذا البنك نفسه. فإن لم يوجد فعلًا، فالقيد رُحِّل إلى "
                + "حساب خاطئ ويحتاج تصحيحًا لا ربطًا."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = BankEntryAlreadyMatched,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That entry is already spoken for", "هذا القيد مرتبط بالفعل"),
            Detail = new LocalizedText(
                "{Entry} is already matched to another statement line. One payment cannot have "
                + "cleared the bank twice, and counting it twice would hide a real difference of "
                + "the same size.",
                "القيد {Entry} مرتبط بسطر آخر في كشف. فالدفعة الواحدة لا يمكن أن تكون مرت "
                + "بالبنك مرتين، واحتسابها مرتين يخفي فرقًا حقيقيًا بالقدر نفسه."),
            Resolution = new LocalizedText(
                "If this line is the right home for it, take the match off the other line first. "
                + "If the bank really did take the money twice, that is a second entry to post "
                + "and then match — and a call to the bank.",
                "إن كان هذا السطر هو موضعه الصحيح، فأزل الارتباط عن السطر الآخر أولًا. أما إن "
                + "كان البنك قد خصم المبلغ مرتين فعلًا، فذلك قيد ثانٍ يُرحَّل ثم يُربط، ومكالمة "
                + "مع البنك."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = BankMatchAmountDiffers,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("The amounts are not the same", "المبلغان غير متطابقين"),
            Detail = new LocalizedText(
                "The bank line is {LineAmount:N2} and the entry is {EntryAmount:N2}, a "
                + "difference of {Difference:N2}. Recorded as asked.",
                "سطر البنك {LineAmount:N2} والقيد {EntryAmount:N2}، بفارق {Difference:N2}. "
                + "وقد سُجّل كما طُلب."),
            Resolution = new LocalizedText(
                "Ordinary when one bank line covers several payments — match the rest to it too. "
                + "If it is a bank charge taken out of a receipt, that charge is a cost and wants "
                + "posting. Either way the reconciliation will not close until the whole "
                + "difference is accounted for.",
                "وهذا معتاد حين يغطي سطر بنكي واحد عدة دفعات، فاربط بقيتها به أيضًا. أما إن كان "
                + "رسمًا بنكيًا اقتُطع من مبلغ محصّل، فذلك الرسم تكلفة تحتاج ترحيلًا. وفي "
                + "الحالتين لن تُغلق المطابقة حتى يُفسَّر الفارق كله."),
            HelpTopic = "finance/bank-reconciliation",
        },
        new()
        {
            Code = CurrencyNotFound,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("No such currency", "لا توجد عملة بهذا الرمز"),
            Detail = new LocalizedText(
                "Nothing in this company is set up as {Currency}, so there is no way to say what "
                + "an amount in it is worth on {Date:d}.",
                "لا يوجد في هذه الشركة ما هو مُعرَّف بالرمز {Currency}، فلا سبيل لمعرفة قيمة "
                + "مبلغ به في {Date:d}."),
            Resolution = new LocalizedText(
                "Add {Currency} under currencies, then give it a rate. If the amount is in the "
                + "company's own currency, leave the currency blank instead.",
                "أضف {Currency} في العملات ثم حدّد له سعر صرف. وإذا كان المبلغ بعملة الشركة "
                + "نفسها، فاترك حقل العملة فارغًا."),
            HelpTopic = "finance/currencies",
        },
        new()
        {
            Code = CurrencyBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("This currency is no longer in use", "هذه العملة لم تعد مستخدمة"),
            Detail = new LocalizedText(
                "{Currency} has been withdrawn, so nothing new may be posted in it. Documents "
                + "already raised in it are untouched and still settle normally.",
                "سُحبت العملة {Currency} من الاستخدام، فلا يمكن ترحيل جديد بها. أما المستندات "
                + "الصادرة بها فلم تتغير ولا تزال تُسوّى كالمعتاد."),
            Resolution = new LocalizedText(
                "Use a currency still in use, or reactivate {Currency} under currencies if it "
                + "was withdrawn by mistake.",
                "استخدم عملة لا تزال مفعّلة، أو أعد تفعيل {Currency} في العملات إن كان سحبها "
                + "قد تم بالخطأ."),
            HelpTopic = "finance/currencies",
        },
        new()
        {
            Code = NoExchangeRate,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("No rate for that day", "لا يوجد سعر صرف لذلك اليوم"),
            Detail = new LocalizedText(
                "{Currency} has no rate starting on or before {Date:d}, and this posting is dated "
                + "{Date:d}. A missing rate is not nought and it is not yesterday's — it is a "
                + "figure nobody has entered.",
                "لا يوجد للعملة {Currency} سعر صرف يبدأ في {Date:d} أو قبله، وهذا الترحيل مؤرخ "
                + "في {Date:d}. والسعر المفقود ليس صفرًا ولا سعر الأمس، بل رقم لم يدخله أحد."),
            Resolution = new LocalizedText(
                "Add a rate for {Currency} starting on or before {Date:d}. Rates may be entered "
                + "ahead of time, so a desk that publishes tomorrow's today is doing the right "
                + "thing.",
                "أضف سعر صرف للعملة {Currency} يبدأ في {Date:d} أو قبله. ويمكن إدخال الأسعار "
                + "مقدمًا، فمن ينشر سعر الغد اليوم إنما يفعل الصواب."),
            HelpTopic = "finance/currencies",
        },
        new()
        {
            Code = CurrencyIsBase,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText(
                "That is the company's own currency", "هذه هي عملة الشركة نفسها"),
            Detail = new LocalizedText(
                "Line {LineNo} is marked {Currency}, which is what this company keeps its books "
                + "in. It has been posted as an ordinary amount, with no conversion.",
                "السطر {LineNo} محدد بالعملة {Currency}، وهي العملة التي تمسك بها الشركة دفاترها. "
                + "وقد رُحِّل كمبلغ عادي دون أي تحويل."),
            Resolution = new LocalizedText(
                "Nothing to do. Leave the currency blank on lines in the company's own currency, "
                + "so a rate is never looked for and never wrongly applied.",
                "لا حاجة لأي إجراء. اترك حقل العملة فارغًا في السطور بعملة الشركة، حتى لا يُبحث "
                + "عن سعر صرف ولا يُطبَّق خطأً."),
            HelpTopic = "finance/currencies",
        },
        new()
        {
            Code = ApplicationDifferentCurrencies,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Different currencies", "عملتان مختلفتان"),
            Detail = new LocalizedText(
                "{FromDocumentNo} is in {FromCurrency} and {ToDocumentNo} is in {ToCurrency}. "
                + "Settling one against the other would have to pick a rate to compare them at, "
                + "and any rate it picked would decide a gain or a loss nobody agreed to.",
                "المستند {FromDocumentNo} بعملة {FromCurrency} والمستند {ToDocumentNo} بعملة "
                + "{ToCurrency}. وتسوية أحدهما بالآخر تستلزم اختيار سعر للمقارنة، وأي سعر "
                + "يُختار سيقرر ربحًا أو خسارة لم يوافق عليها أحد."),
            Resolution = new LocalizedText(
                "Settle each against a document in its own currency. A payment made in one "
                + "currency for an invoice in another is two transactions and a conversion "
                + "between them, and is entered that way.",
                "سوِّ كل مستند بمستند بعملته نفسها. فالدفعة بعملة عن فاتورة بعملة أخرى هي "
                + "عمليتان وتحويل بينهما، وتُدخَل على هذا النحو."),
            HelpTopic = "finance/currencies",
        },
        new()
        {
            Code = NoExchangeDifferenceAccount,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "Nowhere to put the exchange difference", "لا مكان لفرق سعر الصرف"),
            Detail = new LocalizedText(
                "Settling these leaves {Amount:N2} that exists only because the rate moved "
                + "between the two dates, and the setting {SettingKey} says nowhere to put it.",
                "تسوية هذين تترك مبلغ {Amount:N2} نشأ فقط لأن سعر الصرف تحرك بين التاريخين، "
                + "والإعداد {SettingKey} لا يحدد أين يُقيَّد."),
            Resolution = new LocalizedText(
                "Set {SettingKey} in setup. The money was never earned or spent — it is what the "
                + "same foreign amount was worth on two different days — so it belongs on an "
                + "account of its own rather than inside revenue or a cost.",
                "حدّد الإعداد {SettingKey} في الإعدادات. فهذا المبلغ لم يُكتسب ولم يُنفق، وإنما "
                + "هو قيمة المبلغ الأجنبي نفسه في يومين مختلفين، فمكانه حساب مستقل لا داخل "
                + "الإيرادات أو التكاليف."),
            HelpTopic = "finance/currencies",
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
            Code = PartyNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such customer or vendor", "لا يوجد عميل أو مورّد بهذا الرقم"),
            Detail = new LocalizedText(
                "Line {LineNo} names {PartyNo}, and no {PartyKind} in this company carries that number.",
                "السطر {LineNo} يشير إلى {PartyNo}، ولا يوجد {PartyKind} في هذه الشركة بهذا الرقم."),
            Resolution = new LocalizedText(
                "Check the number against the customer or vendor list, or create {PartyNo} first.",
                "تحقق من الرقم في قائمة العملاء أو المورّدين، أو أنشئ {PartyNo} أولاً."),
        },
        new()
        {
            Code = PartyBlocked,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That account is blocked", "هذا الحساب محظور"),
            Detail = new LocalizedText(
                "{PartyNo} {PartyName} has been withdrawn from use, usually because trading with "
                + "them has been stopped.",
                "تم سحب {PartyNo} {PartyName} من الاستخدام، عادةً لإيقاف التعامل معه."),
            Resolution = new LocalizedText(
                "Unblock {PartyNo} if trading has resumed. If this is a payment settling what they "
                + "already owe, an administrator can override -- taking money owed is rarely the "
                + "thing a block was meant to prevent.",
                "ألغِ حظر {PartyNo} إن استُؤنف التعامل. وإذا كان هذا سدادًا لمديونية قائمة، يمكن "
                + "للمسؤول التجاوز، فتحصيل المستحقات نادرًا ما يكون الغرض من الحظر."),
            OverridePermission = "Finance.Party.Override",
            HelpTopic = "finance/customers-and-vendors",
        },
        new()
        {
            Code = CreditLimitExceeded,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("Over the credit limit", "تجاوز حد الائتمان"),
            Detail = new LocalizedText(
                "{PartyNo} {PartyName} owes {Balance:N2} and this would take them to "
                + "{BalanceAfter:N2}, which is {ExcessAmount:N2} over their limit of {CreditLimit:N2}.",
                "مديونية {PartyNo} {PartyName} حاليًا {Balance:N2} وهذه العملية سترفعها إلى "
                + "{BalanceAfter:N2}، أي بزيادة {ExcessAmount:N2} عن حدّه البالغ {CreditLimit:N2}."),
            Resolution = new LocalizedText(
                "Take payment against what is already outstanding, reduce the amount, or raise the "
                + "credit limit on {PartyNo}. Someone with the override permission can let this "
                + "one through, and it will be recorded against their name.",
                "حصّل جزءًا من المديونية القائمة، أو خفّض المبلغ، أو ارفع حد الائتمان لـ {PartyNo}. "
                + "ويمكن لمن يملك صلاحية التجاوز تمرير هذه العملية، وسيُسجَّل ذلك باسمه."),
            OverridePermission = "Finance.Party.Override",
            HelpTopic = "finance/customers-and-vendors",
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
            Code = FiscalYearNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such financial year", "لا توجد سنة مالية بهذا الرمز"),
            Detail = new LocalizedText(
                "No financial year in this company is coded {YearCode}.",
                "لا توجد في هذه الشركة سنة مالية بالرمز {YearCode}."),
            Resolution = new LocalizedText(
                "Check the code against the fiscal calendar.",
                "تحقق من الرمز في التقويم المالي."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = YearAlreadyTransferred,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("That year has already been closed", "تم إقفال السنة بالفعل"),
            Detail = new LocalizedText(
                "The result of {YearCode} was transferred to retained earnings on {EndDate:d}. "
                + "Running it again would move a result that is no longer there and take retained "
                + "earnings the same distance in the wrong direction.",
                "رُحّلت نتيجة السنة {YearCode} إلى الأرباح المبقاة في {EndDate:d}. وإعادة تشغيل "
                + "الإقفال تنقل نتيجة لم تعد قائمة وتحرّك الأرباح المبقاة بنفس المقدار في الاتجاه "
                + "الخطأ."),

            // No override. There is no version of running this twice that is right, so there is
            // nothing for a permission to unlock.
            Resolution = new LocalizedText(
                "If the result itself was wrong, correct it with an entry in the current year. A "
                + "prior-period adjustment belongs in this year's books.",
                "إن كانت النتيجة نفسها خاطئة فصحّحها بقيد في السنة الحالية، فتعديل الفترات "
                + "السابقة مكانه دفاتر السنة الجارية."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = YearLockedBeforeTransfer,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText(
                "That year is locked and its result is still in it",
                "السنة مقفلة ونتيجتها ما زالت فيها"),
            Detail = new LocalizedText(
                "{YearCode} was locked against posting before the year-end transfer was run, so "
                + "the transfer cannot be posted and the result cannot leave the income statement "
                + "accounts.",
                "أُقفلت السنة {YearCode} أمام الترحيل قبل تشغيل قيد الإقفال، فلا يمكن ترحيل القيد "
                + "ولا يمكن للنتيجة أن تغادر حسابات قائمة الدخل."),
            Resolution = new LocalizedText(
                "Reopen {YearCode}, run the transfer, and let the transfer lock it.",
                "أعد فتح السنة {YearCode} وشغّل قيد الإقفال، ودع الإقفال هو ما يغلقها."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = EarlierYearNotTransferred,
            Severity = MessageSeverity.Blocked,
            Title = new LocalizedText("An earlier year is still open", "سنة سابقة لم تُقفل بعد"),
            Detail = new LocalizedText(
                "{EarlierYearCode} ends before {YearCode} begins and its result was never "
                + "transferred, so it is still sitting in the income statement accounts. Closing "
                + "{YearCode} now would sweep it up and report it as part of this year.",
                "السنة {EarlierYearCode} تنتهي قبل بداية {YearCode} ولم تُرحّل نتيجتها، فهي ما "
                + "زالت في حسابات قائمة الدخل. وإقفال {YearCode} الآن يجمعها معها ويعرضها كجزء من "
                + "نتيجة هذه السنة."),
            Resolution = new LocalizedText(
                "Close {EarlierYearCode} first. Years are closed in the order they happened, for "
                + "the same reason they are reported in it.",
                "أقفل السنة {EarlierYearCode} أولاً. فالسنوات تُقفل بترتيب حدوثها، للسبب نفسه "
                + "الذي تُعرض به بذلك الترتيب."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = NoRetainedEarningsAccount,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Nowhere to put the result", "لا يوجد حساب لنتيجة السنة"),
            Detail = new LocalizedText(
                "No retained earnings account is set up, so the result of {YearCode} has nowhere "
                + "to go.",
                "لا يوجد حساب للأرباح المبقاة، فنتيجة السنة {YearCode} بلا وجهة."),
            Resolution = new LocalizedText(
                "Set Finance.General.RetainedEarningsAccount in setup. It is an equity account: "
                + "what the company earned belongs to its owners.",
                "حدّد الإعداد Finance.General.RetainedEarningsAccount. وهو حساب ضمن حقوق الملكية، "
                + "فما كسبته الشركة يعود لأصحابها."),
            HelpTopic = "finance/year-end",
        },
        new()
        {
            Code = NothingToTransfer,
            Severity = MessageSeverity.Warning,
            Title = new LocalizedText("That year had no trading", "لا حركة في هذه السنة"),
            Detail = new LocalizedText(
                "Nothing was posted to an income statement account in {YearCode}, so the transfer "
                + "posted no entries.",
                "لم يُرحّل شيء إلى حسابات قائمة الدخل في السنة {YearCode}، فلم ينشئ الإقفال أي قيد."),
            Resolution = new LocalizedText(
                "The year is marked closed regardless, which is what stops it being asked about "
                + "again. Nothing is wrong; it is worth saying because a year end that posts "
                + "nothing usually means the wrong year was chosen.",
                "وضعت السنة كمقفلة على أي حال، وهو ما يمنع السؤال عنها ثانية. لا خطأ في ذلك، لكنه "
                + "يستحق التنبيه لأن إقفالاً بلا قيود يعني عادةً اختيار السنة الخطأ."),
            HelpTopic = "finance/year-end",
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
            Code = ApplicationEntryNotFound,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("No such ledger entry", "لا يوجد قيد بهذا الرقم"),
            Detail = new LocalizedText(
                "Nothing in this company's {PartyKind} ledger matches the entry named.",
                "لا يوجد في دفتر {PartyKind} بهذه الشركة أي قيد مطابق."),
            Resolution = new LocalizedText(
                "Choose the entry from the account rather than typing it, so only entries that "
                + "exist can be picked.",
                "اختر القيد من كشف الحساب بدلاً من كتابته، حتى لا يمكن اختيار سوى قيود موجودة."),
        },
        new()
        {
            Code = ApplicationEntryClosed,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("That entry is already settled", "هذا القيد مُسوّى بالكامل"),
            Detail = new LocalizedText(
                "{DocumentNo} has nothing outstanding, so there is nothing to apply against it.",
                "لا يوجد رصيد مستحق على {DocumentNo}، فلا شيء لتسويته."),
            Resolution = new LocalizedText(
                "Choose an entry that is still open. If this one was settled in error, unapply the "
                + "application that closed it first.",
                "اختر قيدًا ما زال مفتوحًا. وإذا سُوّي هذا القيد بالخطأ، فألغِ التسوية التي أغلقته أولاً."),
        },
        new()
        {
            Code = ApplicationSameDirection,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Those two entries cannot settle each other", "لا يمكن تسوية هذين القيدين ببعضهما"),
            Detail = new LocalizedText(
                "{FromDocumentNo} and {ToDocumentNo} are both on the same side of the account, so "
                + "applying one to the other would increase what is outstanding rather than reduce it.",
                "القيدان {FromDocumentNo} و{ToDocumentNo} في الجانب نفسه من الحساب، لذا فإن تسوية "
                + "أحدهما بالآخر ستزيد الرصيد المستحق بدلاً من تخفيضه."),
            Resolution = new LocalizedText(
                "Apply a payment or credit memo against an invoice. Two invoices do not settle one "
                + "another.",
                "طبّق سدادًا أو إشعارًا دائنًا على فاتورة. الفاتورتان لا تُسوّي إحداهما الأخرى."),
        },
        new()
        {
            Code = ApplicationDifferentParties,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("Those entries belong to different accounts", "القيدان يخصّان حسابين مختلفين"),
            Detail = new LocalizedText(
                "{FromDocumentNo} belongs to {FromPartyNo} and {ToDocumentNo} to {ToPartyNo}.",
                "القيد {FromDocumentNo} يخص {FromPartyNo} والقيد {ToDocumentNo} يخص {ToPartyNo}."),
            Resolution = new LocalizedText(
                "Apply within one account. Money received from one customer does not settle "
                + "another's invoice, even where the two are related -- if it genuinely should, "
                + "post a transfer between them so the movement is on the record.",
                "طبّق التسوية داخل الحساب الواحد. المبلغ المستلم من عميل لا يُسوّي فاتورة عميل آخر "
                + "حتى لو كانا مرتبطين، وإن لزم ذلك فعلاً فرحّل تحويلاً بينهما ليكون الأمر موثّقًا."),
        },
        new()
        {
            Code = ApplicationTooLarge,
            Severity = MessageSeverity.Error,
            Title = new LocalizedText("More than is outstanding", "أكبر من الرصيد المستحق"),
            Detail = new LocalizedText(
                "{Amount:N2} was offered against {ToDocumentNo}, which has {Outstanding:N2} left, "
                + "drawing on {FromDocumentNo}, which has {Available:N2} unapplied.",
                "تم تقديم {Amount:N2} لتسوية {ToDocumentNo} الذي تبقّى عليه {Outstanding:N2}، "
                + "سحبًا من {FromDocumentNo} الذي لديه {Available:N2} غير مطبّق."),
            Resolution = new LocalizedText(
                "Apply no more than the smaller of the two. Leaving the rest unapplied is correct: "
                + "it stays on the account and can settle the next invoice.",
                "لا تطبّق أكثر من الأصغر بين المبلغين. وترك الباقي دون تطبيق هو التصرف الصحيح، "
                + "إذ يبقى في الحساب لتسوية الفاتورة التالية."),
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
        new()
        {
            // The same confirmation for a journal carrying no document number. Two definitions
            // rather than one, because the catalogue substitutes values and does not compose
            // sentences -- and the single-template version ended every such posting with the
            // words "document ." followed by nothing.
            Code = PostedWithoutDocument,
            Severity = MessageSeverity.Success,
            Title = new LocalizedText("Posted", "تم الترحيل"),
            Detail = new LocalizedText(
                "{EntryCount} entries posted as transaction {TransactionNo}.",
                "تم ترحيل {EntryCount} قيدًا ضمن الحركة {TransactionNo}."),
        },
    ];
}
