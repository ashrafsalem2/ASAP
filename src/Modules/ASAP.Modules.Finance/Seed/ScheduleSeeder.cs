using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Seed;

/// <summary>
/// Gives a new company the statements everybody needs, as editable layouts.
/// </summary>
/// <remarks>
/// <para>
/// The cash flow statement is here rather than in code, and that is the point of the whole
/// feature. Every company's cash flow has a line the last one did not, and a version compiled
/// into the product answers only the general case — which is to say, nobody's.
/// </para>
/// <para>
/// It also means the shipped statements are worked examples. Somebody who wants their own
/// statement opens one of these, sees how a subtotal is written and how a sign is turned, and
/// copies it. A blank screen and a syntax reference is a much worse teacher.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class ScheduleSeeder(AsapDbContext context)
{
    /// <summary>
    /// Adds the shipped layouts, if the company has none by those codes.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many layouts were added.</returns>
    /// <remarks>
    /// Checked per layout rather than "has any". A company that has built its own statements is
    /// exactly the one that would otherwise never receive a new shipped layout, and a company
    /// that has edited a shipped one keeps its edits.
    /// </remarks>
    public async Task<int> SeedAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.Set<AccountSchedule>()
            .Select(static s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var present = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        if (present.Add("CASHFLOW"))
        {
            AddCashFlow(tenantId, companyId);
            added++;
        }

        if (present.Add("PROFIT"))
        {
            AddProfitAndLoss(tenantId, companyId);
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    /// <summary>
    /// Cash flow, by the indirect method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Starts from the result and works back to cash, because that is the statement an auditor
    /// expects and a lender reads. The adjustments are the movements in everything that is not
    /// cash: what customers owe, what is sitting in stock, what the company owes, and what it has
    /// put into fixed assets.
    /// </para>
    /// <para>
    /// The ranges are exhaustive on purpose. Every account in the chart is in exactly one of them,
    /// so the last row is nought by construction rather than by luck — every entry balances, so
    /// what cash did must be the mirror of what everything else did. A cash flow whose check row
    /// is not nought has found a real gap: an account nobody put in a range, usually one added
    /// after the statement was written.
    /// </para>
    /// <para>
    /// The classic add-back of depreciation is not a separate row here, and its absence is the
    /// price of that exactness. Depreciation's other side lands in accumulated depreciation, which
    /// is inside "what is tied up in fixed assets"; adding it again would count it twice and put
    /// the check row out by exactly the depreciation charge. A company that wants the line broken
    /// out splits the fixed asset row in two — see the help topic.
    /// </para>
    /// </remarks>
    private void AddCashFlow(Guid tenantId, Guid companyId)
    {
        var schedule = New(
            tenantId,
            companyId,
            "CASHFLOW",
            "Cash flow statement",
            "قائمة التدفقات النقدية",
            "Where the money went, by the indirect method: the result, adjusted for everything "
            + "that moved without being cash.");

        var order = 0;

        // Every row below is turned, so each one reads as its effect on cash. Receivables going
        // up is money earned and not collected, and shows as a negative. Payables going up is
        // money owed and not yet paid, and shows as a positive. That is what a reader expects,
        // and it is why the sign is turned before any formula runs rather than on the way out.
        Row("R10", "Result for the period", "نتيجة الفترة", ScheduleRowKind.Accounts, "4000..6999", flip: true);

        Blank("R15");
        Heading("R20", "Movements in everything that is not cash", "حركات ما ليس نقدًا");
        Row("R30", "Owed by customers and others", "المستحق على العملاء وغيرهم", ScheduleRowKind.Accounts, "1200..1399", flip: true, indent: 1);
        Row("R40", "Held in stock", "المحتفظ به في المخزون", ScheduleRowKind.Accounts, "1400..1499", flip: true, indent: 1);
        Row("R50", "Tied up in fixed and other assets", "المرتبط بالأصول الثابتة وغيرها", ScheduleRowKind.Accounts, "1500..1999", flip: true, indent: 1);
        Row("R60", "Owed to suppliers, staff and the authority", "المستحق للموردين والموظفين والجهات", ScheduleRowKind.Accounts, "2000..2999", flip: true, indent: 1);
        Row("R70", "Put in or taken out by the owners", "ما أدخله المُلاك أو أخرجوه", ScheduleRowKind.Accounts, "3000..3999", flip: true, indent: 1);

        Blank("R75");
        Row("R80", "Cash this should have generated", "النقد المفترض تولده", ScheduleRowKind.Formula, expression: "R10 + R30 + R40 + R50 + R60 + R70", bold: true);
        Row("R90", "Cash and bank actually moved", "الحركة الفعلية للنقد والبنك", ScheduleRowKind.Accounts, "1000..1199", bold: true);
        Row("R100", "Unexplained", "غير مفسَّر", ScheduleRowKind.Formula, expression: "R90 - R80", bold: true);

        void Row(
            string rowNo,
            string description,
            string arabic,
            ScheduleRowKind kind,
            string? accounts = null,
            string? expression = null,
            bool flip = false,
            bool bold = false,
            int indent = 0)
            => schedule.Lines.Add(Line(
                tenantId, companyId, ++order, rowNo, description, arabic, kind,
                accounts ?? expression, flip, bold, indent));

        void Heading(string rowNo, string description, string arabic)
            => schedule.Lines.Add(Line(
                tenantId, companyId, ++order, rowNo, description, arabic,
                ScheduleRowKind.Heading, null, false, true, 0));

        void Blank(string rowNo)
            => schedule.Lines.Add(Line(
                tenantId, companyId, ++order, rowNo, string.Empty, string.Empty,
                ScheduleRowKind.Heading, null, false, false, 0));
    }

    /// <summary>
    /// A profit and loss, as a worked example of the syntax.
    /// </summary>
    /// <remarks>
    /// The shipped income statement report answers this already. This exists so that somebody who
    /// wants theirs laid out differently — marketing broken out, or a gross margin percentage —
    /// has something to copy rather than a blank screen.
    /// </remarks>
    private void AddProfitAndLoss(Guid tenantId, Guid companyId)
    {
        var schedule = New(
            tenantId,
            companyId,
            "PROFIT",
            "Profit and loss",
            "قائمة الأرباح والخسائر",
            "The result for a period, laid out as an editable example.");

        var order = 0;

        // Revenue is turned, because it is held as a credit and nobody wants to read sales as a
        // negative. Costs are not: they are debits already, and a cost shown as a positive number
        // and then subtracted is how a printed profit and loss reads.
        Row("R10", "Revenue", "الإيرادات", ScheduleRowKind.Accounts, "4000..4899", flip: true);
        Row("R20", "Cost of sales", "تكلفة المبيعات", ScheduleRowKind.Accounts, "5000..5999");
        Row("R30", "Gross profit", "مجمل الربح", ScheduleRowKind.Formula, expression: "R10 - R20", bold: true);

        // A ratio, which is why formulas do division at all. On a month with no sales it comes
        // out blank rather than nought, because nought per cent is a claim and there is nothing
        // to claim it about.
        Row("R40", "Gross margin %", "نسبة مجمل الربح ٪", ScheduleRowKind.Formula, expression: "R30 / R10 * 100", indent: 1);

        Row("R50", "Staff costs", "تكاليف الموظفين", ScheduleRowKind.Accounts, "6100..6199");
        Row("R60", "Other expenses", "مصروفات أخرى", ScheduleRowKind.Accounts, "6200..6999");
        Row("R70", "Other income", "إيرادات أخرى", ScheduleRowKind.Accounts, "4900..4999", flip: true);
        Row("R80", "Result for the period", "نتيجة الفترة", ScheduleRowKind.Formula, expression: "R30 - R50 - R60 + R70", bold: true);

        void Row(
            string rowNo,
            string description,
            string arabic,
            ScheduleRowKind kind,
            string? accounts = null,
            string? expression = null,
            bool flip = false,
            bool bold = false,
            int indent = 0)
            => schedule.Lines.Add(Line(
                tenantId, companyId, ++order, rowNo, description, arabic, kind,
                accounts ?? expression, flip, bold, indent));
    }

    private AccountSchedule New(
        Guid tenantId,
        Guid companyId,
        string code,
        string name,
        string arabic,
        string description)
    {
        var schedule = new AccountSchedule
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = code,
            Name = name,
            NameArabic = arabic,
            Description = description,
        };

        context.Set<AccountSchedule>().Add(schedule);

        return schedule;
    }

    private static AccountScheduleLine Line(
        Guid tenantId,
        Guid companyId,
        int order,
        string rowNo,
        string description,
        string arabic,
        ScheduleRowKind kind,
        string? expression,
        bool flip,
        bool bold,
        int indent)
        => new()
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Order = order,
            RowNo = rowNo,
            Description = description,
            DescriptionArabic = arabic,
            Kind = kind,
            Expression = expression,
            ShowOppositeSign = flip,
            IsBold = bold,
            Indent = indent,
        };
}
