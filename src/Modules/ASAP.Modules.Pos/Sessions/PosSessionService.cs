using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Pos.Sessions;

/// <summary>What a session looks like at a moment in time, without closing it.</summary>
/// <param name="SessionNo">The session read.</param>
/// <param name="StationCode">The till.</param>
/// <param name="CashierName">Who is working it.</param>
/// <param name="OpenedAtUtc">When the drawer was opened.</param>
/// <param name="ReceiptCount">How many receipts have been taken.</param>
/// <param name="NetSales">What has been sold, net of tax.</param>
/// <param name="TaxAmount">Tax charged so far.</param>
/// <param name="OpeningFloat">What the drawer started with.</param>
/// <param name="CashTendered">Cash taken in.</param>
/// <param name="ChangeGiven">Change handed back.</param>
/// <param name="CashRefunded">Cash paid out on returns.</param>
/// <param name="CardTaken">Taken by card, which never reaches the drawer.</param>
/// <param name="OnAccountTaken">Charged to customer accounts.</param>
/// <param name="ExpectedCash">What should be in the drawer.</param>
/// <param name="ReadingNo">Which reading this is. A till read four times before a short count
/// is worth somebody noticing, so the count travels with the answer.</param>
public readonly record struct PosReading(
    string SessionNo,
    string StationCode,
    string? CashierName,
    DateTime OpenedAtUtc,
    int ReceiptCount,
    decimal NetSales,
    decimal TaxAmount,
    decimal OpeningFloat,
    decimal CashTendered,
    decimal ChangeGiven,
    decimal CashRefunded,
    decimal CardTaken,
    decimal OnAccountTaken,
    decimal ExpectedCash,
    int ReadingNo);

/// <summary>What closing a session settled.</summary>
/// <param name="SessionNo">The session closed.</param>
/// <param name="ExpectedCash">What should have been in the drawer.</param>
/// <param name="DeclaredCash">What was counted.</param>
/// <param name="Variance">The difference. Negative is short, positive is over.</param>
/// <param name="TransactionNo">The transaction the difference posted under, if any.</param>
/// <param name="Reading">The final figures, which are what a Z reading prints.</param>
public readonly record struct PosSessionClosed(
    string SessionNo,
    decimal ExpectedCash,
    decimal DeclaredCash,
    decimal Variance,
    long? TransactionNo,
    PosReading Reading);

/// <summary>
/// Opens, reads and closes a till session.
/// </summary>
/// <remarks>
/// <para>
/// Nothing about a sale lives here. A session is the container the money is counted in, and the
/// only thing it posts is the difference between what was counted and what was taken — because
/// every receipt already put its own takings where they belong, at the moment they were taken.
/// </para>
/// <para>
/// That is worth stating, because the alternative is common and wrong: hold the day's takings in
/// a clearing account and move them to cash at close. It reads tidily and it means the cash
/// account is a lie for the length of a shift, which is exactly when somebody asks how much is in
/// the shop.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection a close pushed past.</param>
/// <param name="documents">Posts the difference a count leaves behind.</param>
/// <param name="branches">Says which branch a till stands in.</param>
/// <param name="numbers">Issues the session number.</param>
/// <param name="setup">Supplies the number series and the variance account.</param>
/// <param name="userContext">Names the cashier.</param>
/// <param name="clock">Supplies the time and the business date.</param>
/// <param name="logger">Records sessions opened and closed.</param>
public sealed class PosSessionService(
    AsapDbContext context,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    DocumentPostingService documents,
    Stations.StationBranchLookup branches,
    INumberSeriesService numbers,
    ISetupService setup,
    IUserContext userContext,
    IClock clock,
    ILogger<PosSessionService> logger)
{
    /// <summary>
    /// Opens a drawer at a till.
    /// </summary>
    /// <param name="stationCode">The till to open.</param>
    /// <param name="openingFloat">What is in the drawer to start with.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The session, or every reason it could not be opened.</returns>
    public async Task<Result<PosSession>> OpenAsync(
        string stationCode,
        decimal openingFloat,
        CancellationToken cancellationToken = default)
    {
        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == stationCode, cancellationToken)
            .ConfigureAwait(false);

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["StationCode"] = stationCode,
            ["StationName"] = station?.Name,
        };

        if (station is null)
        {
            return Result<PosSession>.Failure(
                messages.Render(PosMessages.StationNotFound, arguments));
        }

        if (station.IsBlocked)
        {
            return Result<PosSession>.Failure(
                messages.Render(PosMessages.StationBlocked, arguments));
        }

        // One drawer cannot be counted twice, so it cannot be worked by two sessions at once.
        var alreadyOpen = await context.Set<PosSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.StationCode == stationCode && s.Status == PosSessionStatus.Open,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyOpen is not null)
        {
            arguments["SessionNo"] = alreadyOpen.No;
            arguments["CashierName"] = alreadyOpen.CashierName;

            return Result<PosSession>.Failure(
                messages.Render(PosMessages.SessionAlreadyOpen, arguments));
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync($"{PosModule.Id}.Sessions.NumberSeries", "POS-SESS", cancellationToken)
            .ConfigureAwait(false);

        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PosSession>.FailureFrom(numbered);
        }

        var session = new PosSession
        {
            No = numbered.Value,
            StationCode = station.Code,
            CashierId = userContext.UserId,
            CashierName = userContext.DisplayName ?? userContext.UserName,
            OpenedAtUtc = clock.UtcNow,
            BusinessDate = today,
            OpeningFloat = openingFloat,
            Status = PosSessionStatus.Open,
        };

        context.Set<PosSession>().Add(session);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Opened till session {SessionNo} at {StationCode} with a float of {OpeningFloat}.",
            session.No,
            session.StationCode,
            openingFloat);

        return Result<PosSession>.Success(session);
    }

    /// <summary>
    /// Reads a session without closing it, which is what an X reading is.
    /// </summary>
    /// <remarks>
    /// Deliberately available while trading. A supervisor checking a drawer mid-shift is doing
    /// the thing that catches a problem while it is still one receipt wide, and a system that
    /// only tells them at the end has chosen the accountant's convenience over the shop's.
    /// </remarks>
    /// <param name="sessionNo">The session to read.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The figures, or the reason the session could not be read.</returns>
    public async Task<Result<PosReading>> ReadAsync(
        string sessionNo,
        CancellationToken cancellationToken = default)
    {
        var session = await FindAsync(sessionNo, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<PosReading>.Failure(NotFound(sessionNo));
        }

        session.ReadingCount++;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PosReading>.Success(Read(session));
    }

    /// <summary>
    /// Counts the drawer and finishes the session, which is what a Z reading is.
    /// </summary>
    /// <param name="sessionNo">The session to close.</param>
    /// <param name="declaredCash">What the cashier counted.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was settled, or every reason it could not be closed.</returns>
    public async Task<Result<PosSessionClosed>> CloseAsync(
        string sessionNo,
        decimal declaredCash,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var session = await FindAsync(sessionNo, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<PosSessionClosed>.Failure(NotFound(sessionNo));
        }

        var found = new List<AsapMessage>();

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionNo"] = session.No,
            ["StationCode"] = session.StationCode,
        };

        if (!session.IsOpen)
        {
            arguments["ClosedAt"] = session.ClosedAtUtc;

            return Result<PosSessionClosed>.Failure(
                messages.Render(PosMessages.SessionClosed, arguments));
        }

        // Anything set aside and unpaid belongs to this turn. Left behind it becomes the next
        // cashier's problem, attached to a drawer that has already been counted.
        var parked = await context.Set<PosReceipt>()
            .CountAsync(
                r => r.SessionId == session.Id && r.Status == PosReceiptStatus.Parked,
                cancellationToken)
            .ConfigureAwait(false);

        if (parked > 0)
        {
            arguments["ParkedCount"] = parked;

            var parkedMessage = Raise(PosMessages.ParkedSalesRemain, arguments, heldOverridePermissions);

            found.Add(parkedMessage);

            if (parkedMessage.IsFailure)
            {
                return Result<PosSessionClosed>.Failure(found);
            }
        }

        session.DeclaredCash = declaredCash;

        var variance = session.Variance ?? 0m;
        long? transactionNo = null;

        if (variance != 0m)
        {
            var posted = await PostVarianceAsync(session, variance, arguments, cancellationToken)
                .ConfigureAwait(false);

            if (posted.Failed)
            {
                return Result<PosSessionClosed>.FailureFrom(posted);
            }

            transactionNo = posted.Value;

            arguments["DeclaredCash"] = declaredCash;
            arguments["ExpectedCash"] = session.ExpectedCash;
            arguments["Variance"] = variance;
            arguments["OpeningFloat"] = session.OpeningFloat;
            arguments["CashTendered"] = session.CashTendered;
            arguments["ChangeGiven"] = session.ChangeGiven;

            found.Add(messages.Render(PosMessages.CashVariance, arguments));
        }

        session.Status = PosSessionStatus.Closed;
        session.ClosedAtUtc = clock.UtcNow;
        session.ClosedBy = userContext.UserId;
        session.ClosingTransactionNo = transactionNo;

        overrides.Record(found, "Pos.Session", session.No, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Closed till session {SessionNo}: expected {ExpectedCash}, counted {DeclaredCash}, "
            + "variance {Variance}.",
            session.No,
            session.ExpectedCash,
            declaredCash,
            variance);

        return Result<PosSessionClosed>.Success(
            new PosSessionClosed(
                session.No,
                session.ExpectedCash,
                declaredCash,
                variance,
                transactionNo,
                Read(session)),
            found);
    }

    /// <summary>
    /// Moves the cash account onto what was actually counted.
    /// </summary>
    /// <remarks>
    /// Short or over, the entry has the same shape: the drawer is right and the ledger is wrong,
    /// so the ledger moves. Refusing to post a difference would leave the cash account describing
    /// money that is not in the building, which is worse than recording that somebody miscounted.
    /// </remarks>
    private async Task<Result<long>> PostVarianceAsync(
        PosSession session,
        decimal variance,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var cashAccount = await AccountAsync($"{PosModule.Id}.Posting.CashAccount", cancellationToken)
            .ConfigureAwait(false);

        var varianceAccount = await AccountAsync($"{PosModule.Id}.Posting.VarianceAccount", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(varianceAccount) || string.IsNullOrWhiteSpace(cashAccount))
        {
            arguments["Variance"] = variance;

            return Result<long>.Failure(
                messages.Render(PosMessages.NoVarianceAccount, arguments));
        }

        var description = variance < 0m
            ? $"{session.StationCode} {session.No} — till short"
            : $"{session.StationCode} {session.No} — till over";

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: session.No,
                    Lines:
                    [
                        // A positive variance means more was counted than taken, so the drawer
                        // holds more than the ledger says and cash goes up.
                        new PostJournalLine(cashAccount, variance, description),
                        new PostJournalLine(varianceAccount, -variance, description),
                    ],
                    SourceCode: "POS",

                    // The till module owns both accounts. Nobody keyed this.
                    IsManualEntry: false,
                    DocumentNo: session.No,
                    Description: description,

                    // A till difference belongs to the till's shop. Charged centrally it becomes
                    // a single company-wide number nobody owns, which is the state in which
                    // small differences stay small differences for years.
                    BranchId: await branches
                        .BranchOfAsync(session.StationCode, cancellationToken)
                        .ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);

        return posted.Failed
            ? Result<long>.FailureFrom(posted)
            : Result<long>.Success(posted.Value.TransactionNo);
    }

    private static PosReading Read(PosSession session)
        => new(
            session.No,
            session.StationCode,
            session.CashierName,
            session.OpenedAtUtc,
            session.ReceiptCount,
            session.NetSales,
            session.TaxAmount,
            session.OpeningFloat,
            session.CashTendered,
            session.ChangeGiven,
            session.CashRefunded,
            session.CardTaken,
            session.OnAccountTaken,
            session.ExpectedCash,
            session.ReadingCount);

    private Task<PosSession?> FindAsync(string sessionNo, CancellationToken cancellationToken)
        => context.Set<PosSession>().FirstOrDefaultAsync(s => s.No == sessionNo, cancellationToken);

    private AsapMessage NotFound(string sessionNo)
        => messages.Render(
            PosMessages.SessionNotFound,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SessionNo"] = sessionNo,
            });

    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        IReadOnlySet<string>? held)
    {
        var rendered = messages.Render(code, arguments);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
    }

    private async Task<string> SeriesCodeAsync(string key, string fallback, CancellationToken cancellationToken)
        => await setup.GetAsync<string>(key, cancellationToken).ConfigureAwait(false) ?? fallback;

    private async Task<string?> AccountAsync(string key, CancellationToken cancellationToken)
        => await setup.GetAsync<string>(key, cancellationToken).ConfigureAwait(false);
}
