using ASAP.Modules.Finance.Banking;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Payments;

/// <summary>
/// Proposes vendor payments, writes the bank file, and posts what was paid.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists to prevent is paying the same invoice twice. Every step checks for it
/// a different way. Proposal leaves out any invoice already on a run that has not been posted or
/// cancelled, so two runs made on the same morning cannot both carry it. Posting checks each
/// invoice is still owed what the run pays, so an invoice somebody settled by hand after the file
/// was made is caught before the ledger records it twice.
/// </para>
/// <para>
/// The second failure is paying the right invoice to the wrong account. Each line copies the
/// vendor's IBAN when the run is proposed, and export refuses a line whose vendor card now says
/// something different. New bank details announced by email just before a payment run is the
/// commonest form payment fraud takes, and a run that followed the vendor card to the last moment
/// would carry it straight to the bank.
/// </para>
/// <para>
/// Credit notes are not netted against invoices here. Apply them first, on the vendor's account;
/// what is then left open is what gets paid. Netting inside the run would mean deciding which
/// invoice a credit note settles, and that decision belongs to whoever agreed the credit.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="documents">Posts the payments.</param>
/// <param name="applications">Settles the invoices against the payments.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the run number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who proposed, exported and posted.</param>
/// <param name="clock">Says what today is.</param>
/// <param name="logger">Records what was paid.</param>
public sealed class PaymentRunService(
    AsapDbContext context,
    DocumentPostingService documents,
    PartyApplicationService applications,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock,
    ILogger<PaymentRunService> logger)
{
    /// <summary>The runs, most recent first.</summary>
    /// <param name="take">How many.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The runs, without their files.</returns>
    public async Task<IReadOnlyList<PaymentRun>> ListAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
        => await context.Set<PaymentRun>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .ThenInclude(l => l.Invoices)
            .OrderByDescending(r => r.PaymentDate)
            .ThenByDescending(r => r.No)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>One run, its lines and the invoices each pays.</summary>
    /// <param name="no">The run number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The run, or null.</returns>
    public Task<PaymentRun?> LoadAsync(string no, CancellationToken cancellationToken = default)
        => context.Set<PaymentRun>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .ThenInclude(l => l.Invoices)
            .FirstOrDefaultAsync(r => r.No == no, cancellationToken);

    /// <summary>
    /// Proposes paying everything due by a date from one bank account.
    /// </summary>
    /// <param name="bankAccountCode">The account the money leaves.</param>
    /// <param name="paymentDate">The day the bank should pay.</param>
    /// <param name="dueByDate">Invoices due on or before this are proposed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The draft run, and anything worth saying about what was left out.</returns>
    public async Task<Result<PaymentRun>> ProposeAsync(
        string bankAccountCode,
        DateOnly paymentDate,
        DateOnly dueByDate,
        CancellationToken cancellationToken = default)
    {
        var code = bankAccountCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var bank = await context.Set<BankAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == code && b.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (bank is null)
        {
            return Result<PaymentRun>.Failure(
                messages.Render(FinanceMessages.PaymentBankAccountNotFound, Args(("BankAccountCode", code))));
        }

        if (!Iban.IsValid(bank.Iban))
        {
            return Result<PaymentRun>.Failure(
                messages.Render(FinanceMessages.PaymentBankAccountHasNoIban, Args(("BankAccountCode", code))));
        }

        var baseCurrency = await BaseCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var currency = bank.CurrencyCode is { Length: > 0 } held ? held.ToUpperInvariant() : baseCurrency;
        var foreign = !string.Equals(currency, baseCurrency, StringComparison.OrdinalIgnoreCase);

        var found = new List<AsapMessage>();

        // Invoices only: an open payable with money still owed on it. Credit notes are applied on
        // the vendor's account before a run, not guessed at inside one.
        var due = await context.Set<VendorLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.IsOpen && e.RemainingAmount < 0m && e.DueDate <= dueByDate)
            .OrderBy(e => e.PartyNo)
            .ThenBy(e => e.DueDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var onLiveRuns = (await (
                    from invoice in context.Set<PaymentRunInvoice>().AsNoTracking()
                    join line in context.Set<PaymentRunLine>().AsNoTracking() on invoice.PaymentRunLineId equals line.Id
                    join live in context.Set<PaymentRun>().AsNoTracking() on line.PaymentRunId equals live.Id
                    where live.Status == PaymentRunStatus.Draft || live.Status == PaymentRunStatus.Exported
                    select invoice.VendorLedgerEntryId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var inCurrency = due
            .Where(e => string.Equals(e.CurrencyCode ?? baseCurrency, currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var otherCurrency = due.Count - inCurrency.Count;

        if (otherCurrency > 0)
        {
            found.Add(messages.Render(
                FinanceMessages.PaymentOtherCurrencyLeftOut,
                Args(("Count", otherCurrency), ("CurrencyCode", currency))));
        }

        var vendorNos = inCurrency.Select(static e => e.PartyNo).Distinct().ToList();

        var vendors = await context.Set<Vendor>()
            .AsNoTracking()
            .Where(v => vendorNos.Contains(v.No))
            .ToDictionaryAsync(static v => v.No, cancellationToken)
            .ConfigureAwait(false);

        var run = new PaymentRun
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            No = "pending",
            BankAccountCode = bank.Code,
            PaymentDate = paymentDate,
            DueByDate = dueByDate,
            CurrencyCode = currency,
            CreatedByUserName = user.DisplayName ?? user.UserName,
        };

        var lineNo = 0;

        foreach (var group in inCurrency
                     .Where(e => !onLiveRuns.Contains(e.Id))
                     .GroupBy(static e => e.PartyNo))
        {
            if (!vendors.TryGetValue(group.Key, out var vendor))
            {
                continue;
            }

            if (vendor.IsBlocked)
            {
                found.Add(messages.Render(
                    FinanceMessages.PaymentBlockedVendorLeftOut,
                    Args(("VendorNo", vendor.No), ("VendorName", vendor.Name))));
                continue;
            }

            lineNo += 10;

            var invoices = group
                .Select(e => new PaymentRunInvoice
                {
                    TenantId = run.TenantId,
                    CompanyId = run.CompanyId,
                    VendorLedgerEntryId = e.Id,
                    DocumentNo = e.DocumentNo ?? e.ExternalDocumentNo,
                    DueDate = e.DueDate,
                    Amount = Math.Abs(foreign ? e.RemainingAmountInCurrency ?? 0m : e.RemainingAmount),
                })
                .Where(static i => i.Amount > 0m)
                .ToList();

            if (invoices.Count == 0)
            {
                continue;
            }

            run.Lines.Add(new PaymentRunLine
            {
                TenantId = run.TenantId,
                CompanyId = run.CompanyId,
                LineNo = lineNo,
                VendorNo = vendor.No,
                VendorName = vendor.Name,
                Iban = Iban.Normalise(vendor.Iban),
                Bic = vendor.Bic?.Trim().ToUpperInvariant(),
                Reference = Reference(invoices),
                Amount = invoices.Sum(static i => i.Amount),
                Invoices = invoices,
            });
        }

        if (run.Lines.Count == 0)
        {
            found.Add(messages.Render(
                FinanceMessages.NothingDueToPay,
                Args(("CurrencyCode", currency), ("DueByDate", dueByDate))));

            return Result<PaymentRun>.Failure(found);
        }

        var seriesCode = await setup
            .GetAsync<string>($"{FinanceModule.Id}.PaymentRun.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "PAYRUN";

        var numbered = await numbers.NextAsync(seriesCode, paymentDate, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PaymentRun>.FailureFrom(numbered);
        }

        run.No = numbered.Value;

        context.Set<PaymentRun>().Add(run);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PaymentRun>.Success(
            (await LoadAsync(run.No, cancellationToken).ConfigureAwait(false))!,
            found);
    }

    /// <summary>Takes one vendor off a draft run.</summary>
    /// <param name="no">The run number.</param>
    /// <param name="lineNo">The line.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The run, or why the line could not be taken off.</returns>
    public async Task<Result<PaymentRun>> RemoveLineAsync(
        string no,
        int lineNo,
        CancellationToken cancellationToken = default)
    {
        var run = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return Result<PaymentRun>.Failure(NotFound(no));
        }

        if (run.Status is not PaymentRunStatus.Draft)
        {
            // An exported file may already be at the bank. A line taken off now is a payment the
            // ledger forgets and the bank still makes.
            return Result<PaymentRun>.Failure(WrongStage(run));
        }

        var line = run.Lines.FirstOrDefault(l => l.LineNo == lineNo);

        if (line is not null)
        {
            context.Set<PaymentRunInvoice>().RemoveRange(line.Invoices);
            context.Set<PaymentRunLine>().Remove(line);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Removal is a soft delete, so the rows stay in the change tracker, and the next load of
            // this run in the same unit of work would put them straight back on it. Let them go.
            foreach (var invoice in line.Invoices.ToList())
            {
                context.Entry(invoice).State = EntityState.Detached;
            }

            context.Entry(line).State = EntityState.Detached;
            context.Entry(run).State = EntityState.Detached;
        }

        return Result<PaymentRun>.Success((await LoadAsync(no, cancellationToken).ConfigureAwait(false))!);
    }

    /// <summary>
    /// Writes the bank file and freezes the run.
    /// </summary>
    /// <param name="no">The run number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file, or every reason it could not be written.</returns>
    public async Task<Result<PaymentFile>> ExportAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var run = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return Result<PaymentFile>.Failure(NotFound(no));
        }

        if (run.Status is not PaymentRunStatus.Draft)
        {
            return Result<PaymentFile>.Failure(WrongStage(run));
        }

        var bank = await context.Set<BankAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == run.BankAccountCode, cancellationToken)
            .ConfigureAwait(false);

        if (bank is null || !Iban.IsValid(bank.Iban))
        {
            return Result<PaymentFile>.Failure(messages.Render(
                bank is null ? FinanceMessages.PaymentBankAccountNotFound : FinanceMessages.PaymentBankAccountHasNoIban,
                Args(("BankAccountCode", run.BankAccountCode))));
        }

        var vendorNos = run.Lines.Select(static l => l.VendorNo).ToList();

        var current = await context.Set<Vendor>()
            .AsNoTracking()
            .Where(v => vendorNos.Contains(v.No))
            .ToDictionaryAsync(static v => v.No, static v => Iban.Normalise(v.Iban), cancellationToken)
            .ConfigureAwait(false);

        var found = new List<AsapMessage>();

        foreach (var line in run.Lines.OrderBy(static l => l.LineNo))
        {
            var target = MessageTarget.OnField($"Lines[{line.LineNo}]");
            var arguments = Args(("VendorNo", line.VendorNo), ("VendorName", line.VendorName));

            if (!Iban.IsValid(line.Iban))
            {
                found.Add(messages.Render(FinanceMessages.VendorIbanInvalid, arguments, target));
                continue;
            }

            var now = current.GetValueOrDefault(line.VendorNo) ?? string.Empty;

            if (!string.Equals(now, line.Iban, StringComparison.Ordinal))
            {
                arguments["ProposedIban"] = line.Iban;
                arguments["CurrentIban"] = now.Length == 0 ? "—" : now;

                found.Add(messages.Render(FinanceMessages.VendorIbanChangedSinceProposal, arguments, target));
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PaymentFile>.Failure(found);
        }

        var company = await context.Set<Company>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == run.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        var file = PaymentFileWriter.Write(
            new PaymentFileHeader(
                run.No,
                clock.UtcNow,
                company?.Name ?? bank.Name,
                bank.Iban!,
                bank.Bic,
                run.PaymentDate,
                run.CurrencyCode),
            [.. run.Lines
                .OrderBy(static l => l.LineNo)
                .Select(l => new PaymentTransfer(
                    $"{run.No}-{l.LineNo}",
                    l.Amount,
                    l.VendorName,
                    l.Iban!,
                    l.Bic,
                    l.Reference))]);

        run.Status = PaymentRunStatus.Exported;
        run.ExportedAtUtc = clock.UtcNow;
        run.ExportedByUserName = user.DisplayName ?? user.UserName;
        run.FileContent = file.Content;
        run.FileSha256 = file.Sha256;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Payment run {RunNo} exported: {Count} transfers, {Total} {Currency}, SHA-256 {Hash}.",
            run.No,
            file.TransferCount,
            file.ControlSum,
            run.CurrencyCode,
            file.Sha256);

        return Result<PaymentFile>.Success(file);
    }

    /// <summary>
    /// Records what the bank paid, and settles the invoices it paid.
    /// </summary>
    /// <param name="no">The run number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The posted run, or every reason it could not be posted.</returns>
    public async Task<Result<PaymentRun>> PostAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var run = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return Result<PaymentRun>.Failure(NotFound(no));
        }

        if (run.Status is not PaymentRunStatus.Exported)
        {
            return Result<PaymentRun>.Failure(WrongStage(run));
        }

        var bank = await context.Set<BankAccount>()
            .AsNoTracking()
            .FirstAsync(b => b.Code == run.BankAccountCode, cancellationToken)
            .ConfigureAwait(false);

        var baseCurrency = await BaseCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var foreign = !string.Equals(run.CurrencyCode, baseCurrency, StringComparison.OrdinalIgnoreCase);

        var entryIds = run.Lines.SelectMany(static l => l.Invoices).Select(static i => i.VendorLedgerEntryId).ToList();

        var entries = await context.Set<VendorLedgerEntry>()
            .AsNoTracking()
            .Where(e => entryIds.Contains(e.Id))
            .ToDictionaryAsync(static e => e.Id, cancellationToken)
            .ConfigureAwait(false);

        // Checked again now, because a day or a week has passed since the file was made. Anything
        // settled another way in the meantime is caught here, before the ledger records it twice.
        var found = new List<AsapMessage>();

        foreach (var line in run.Lines)
        {
            foreach (var invoice in line.Invoices)
            {
                var entry = entries.GetValueOrDefault(invoice.VendorLedgerEntryId);

                var remaining = entry is null || !entry.IsOpen
                    ? 0m
                    : Math.Abs(foreign ? entry.RemainingAmountInCurrency ?? 0m : entry.RemainingAmount);

                if (remaining < invoice.Amount)
                {
                    found.Add(messages.Render(
                        FinanceMessages.PaymentInvoiceSettledMeanwhile,
                        Args(
                            ("DocumentNo", invoice.DocumentNo),
                            ("VendorNo", line.VendorNo),
                            ("RemainingAmount", remaining),
                            ("Amount", invoice.Amount))));
                }
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PaymentRun>.Failure(found);
        }

        var description = $"Payment run {run.No}";
        var currencyCode = foreign ? run.CurrencyCode : null;

        var lines = new List<PostJournalLine>();

        foreach (var line in run.Lines.OrderBy(static l => l.LineNo))
        {
            lines.Add(new PostJournalLine(
                line.VendorNo,
                line.Amount,
                $"{line.VendorName} — {line.Reference}",
                AccountType: JournalAccountType.Vendor,
                CurrencyCode: currencyCode));
        }

        lines.Add(new PostJournalLine(bank.GlAccountNo, -run.TotalAmount, description, CurrencyCode: currencyCode));

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: "PAYRUN",
                    Lines: lines,
                    SourceCode: "PAYRUN",
                    IsManualEntry: false,
                    PartyKind: PartyKind.Vendor,
                    DocumentType: GlDocumentType.Payment,
                    DocumentNo: run.No,
                    Description: description,
                    PostingDate: run.PaymentDate),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<PaymentRun>.FailureFrom(posted);
        }

        run.Status = PaymentRunStatus.Posted;
        run.TransactionNo = posted.Value.TransactionNo;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The payments are in; now each is set against the invoices it paid. A refusal here does
        // not undo the posting — the money went — it leaves an open payment on the vendor's account
        // to apply by hand, and says so.
        var payments = await context.Set<VendorLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.TransactionNo == posted.Value.TransactionNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var notes = new List<AsapMessage>();

        foreach (var line in run.Lines.OrderBy(static l => l.LineNo))
        {
            var payment = payments.Find(p => p.PartyNo == line.VendorNo);

            if (payment is null)
            {
                continue;
            }

            foreach (var invoice in line.Invoices.OrderBy(static i => i.DueDate))
            {
                var applied = await applications
                    .ApplyAsync(PartyKind.Vendor, payment.Id, invoice.VendorLedgerEntryId, invoice.Amount, cancellationToken)
                    .ConfigureAwait(false);

                if (applied.Failed)
                {
                    notes.AddRange(applied.Messages.Select(static m => m));
                }
            }
        }

        logger.LogInformation(
            "Payment run {RunNo} posted under transaction {TransactionNo}.",
            run.No,
            posted.Value.TransactionNo);

        // Application refusals are returned as they are; the run itself is posted either way.
        return Result<PaymentRun>.Success(
            (await LoadAsync(no, cancellationToken).ConfigureAwait(false))!,
            [.. posted.Messages, .. notes.Select(static m => m with { Severity = MessageSeverity.Warning })]);
    }

    /// <summary>Abandons a run that has not posted.</summary>
    /// <param name="no">The run number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The run, or why it could not be abandoned.</returns>
    public async Task<Result<PaymentRun>> CancelAsync(
        string no,
        CancellationToken cancellationToken = default)
    {
        var run = await TrackedAsync(no, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return Result<PaymentRun>.Failure(NotFound(no));
        }

        if (run.Status is PaymentRunStatus.Posted or PaymentRunStatus.Cancelled)
        {
            return Result<PaymentRun>.Failure(WrongStage(run));
        }

        run.Status = PaymentRunStatus.Cancelled;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PaymentRun>.Success((await LoadAsync(no, cancellationToken).ConfigureAwait(false))!);
    }

    /// <summary>What goes on the vendor's statement: the invoices, as many as fit.</summary>
    private static string Reference(IReadOnlyList<PaymentRunInvoice> invoices)
    {
        var numbers = invoices.Select(static i => i.DocumentNo).Where(static n => n is { Length: > 0 }).ToList();

        if (numbers.Count == 0)
        {
            return $"{invoices.Count} invoice(s)";
        }

        var text = string.Join(", ", numbers);

        return text.Length <= 140 ? text : $"{text[..125]}… +{numbers.Count}";
    }

    private async Task<string> BaseCurrencyAsync(CancellationToken cancellationToken)
    {
        var companyId = tenancy.RequireCompanyId();

        return await context.Set<Company>()
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(static c => c.BaseCurrencyCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "SAR";
    }

    private Task<PaymentRun?> TrackedAsync(string no, CancellationToken cancellationToken)
        => context.Set<PaymentRun>()
            .Include(r => r.Lines)
            .ThenInclude(l => l.Invoices)
            .FirstOrDefaultAsync(r => r.No == no, cancellationToken);

    private AsapMessage NotFound(string no)
        => messages.Render(FinanceMessages.PaymentRunNotFound, Args(("RunNo", no)));

    private AsapMessage WrongStage(PaymentRun run)
        => messages.Render(
            FinanceMessages.PaymentRunWrongStage,
            Args(("RunNo", run.No), ("Status", run.Status.ToString())));

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
