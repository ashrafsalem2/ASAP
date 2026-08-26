using ASAP.Modules.Finance.Accounts;
using ASAP.Platform.Core.Dimensions;

namespace ASAP.Modules.Finance.Posting;

/// <summary>
/// What the posting engine needs to know about an account, resolved before validation begins.
/// </summary>
/// <remarks>
/// A flattened view rather than the entity itself, so the validation rules are pure logic that
/// can be exercised without a database. Every rule below is one that has, somewhere, been the
/// cause of a set of books that would not reconcile.
/// </remarks>
/// <param name="Id">The account key.</param>
/// <param name="No">The account number.</param>
/// <param name="Name">The account name.</param>
/// <param name="AccountType">Whether it takes entries or only shapes the report.</param>
/// <param name="IsBlocked">Whether it has been withdrawn from use.</param>
/// <param name="AllowsDirectPosting">Whether a person may post to it by hand.</param>
/// <param name="RequiredDimensionIds">
/// Dimensions this particular account demands, over and above whatever the company demands of
/// every entry. Held as identifiers rather than a flag because the useful case is specific: every
/// cost account must name a department, while a bank balance need not, and expressing that as a
/// boolean loses the distinction.
/// </param>
public sealed record PostingAccountView(
    Guid Id,
    string No,
    string Name,
    GlAccountType AccountType,
    bool IsBlocked,
    bool AllowsDirectPosting,
    IReadOnlySet<Guid>? RequiredDimensionIds = null)
{
    /// <summary>Dimensions this account demands.</summary>
    public IReadOnlySet<Guid> RequiredDimensions => RequiredDimensionIds ?? EmptyDimensions;

    private static readonly HashSet<Guid> EmptyDimensions = [];

    /// <summary>Whether an entry may land here at all.</summary>
    public bool IsPostable => AccountType is GlAccountType.Posting && !IsBlocked;

    /// <summary>Builds a view from an account entity.</summary>
    /// <param name="account">The account.</param>
    /// <param name="companyDimensionIds">
    /// Every dimension defined for the company. Used when the account is flagged as requiring
    /// dimensions, which means all of them.
    /// </param>
    public static PostingAccountView From(GlAccount account, IReadOnlySet<Guid>? companyDimensionIds = null)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new PostingAccountView(
            account.Id,
            account.No,
            account.Name,
            account.AccountType,
            account.IsBlocked,
            account.AllowsDirectPosting,
            account.RequiresDimensions ? companyDimensionIds : null);
    }
}

/// <summary>One line about to be posted, with its accounts already resolved.</summary>
/// <param name="LineNo">Position in the batch, used to point the user at the right row.</param>
/// <param name="PostingDate">The date the entry will be reported in.</param>
/// <param name="Amount">The signed amount. Positive debits, negative credits.</param>
/// <param name="Account">The account it posts to, or null when the line names none.</param>
/// <param name="BalancingAccount">
/// What it balances against. When present the line stands alone, producing two entries that net
/// to nothing, and so does not need another line to balance it.
/// </param>
/// <param name="Dimensions">The dimension combination the entry will carry.</param>
/// <param name="DocumentNo">The document number.</param>
/// <param name="Description">What the entry will say.</param>
public sealed record PostingLineView(
    int LineNo,
    DateOnly PostingDate,
    decimal Amount,
    PostingAccountView? Account,
    PostingAccountView? BalancingAccount = null,
    DimensionCombination Dimensions = default,
    string? DocumentNo = null,
    string? Description = null);

/// <summary>Whether a date may be posted to, and why not when it may not.</summary>
public enum PeriodAvailability
{
    /// <summary>The period is open.</summary>
    Open = 0,

    /// <summary>No period in the fiscal calendar covers the date.</summary>
    NotDefined = 1,

    /// <summary>The period covering the date has been closed.</summary>
    PeriodClosed = 2,

    /// <summary>The financial year covering the date has been closed.</summary>
    YearClosed = 3,
}

/// <summary>What the fiscal calendar says about one date.</summary>
/// <param name="Availability">Whether the date may be posted to.</param>
/// <param name="PeriodName">The period covering it, for the message.</param>
/// <param name="FiscalYearCode">The year covering it, for the message.</param>
public readonly record struct PeriodStatus(
    PeriodAvailability Availability,
    string? PeriodName = null,
    string? FiscalYearCode = null)
{
    /// <summary>A date that may be posted to.</summary>
    public static PeriodStatus Open(string periodName, string fiscalYearCode)
        => new(PeriodAvailability.Open, periodName, fiscalYearCode);
}

/// <summary>A dimension that some or all entries must carry a value for.</summary>
/// <param name="Id">The dimension key.</param>
/// <param name="Code">Its code, for example <c>DEPARTMENT</c>.</param>
/// <param name="Name">Its name, for the message.</param>
/// <param name="IsCompanyWide">
/// True when every entry in the company must carry it. False when it is demanded only by the
/// accounts that name it, which is the more common arrangement: a department is required on
/// costs and meaningless on a bank balance.
/// </param>
public readonly record struct MandatoryDimensionView(
    Guid Id,
    string Code,
    string Name,
    bool IsCompanyWide = true);

/// <summary>
/// Everything outside the lines themselves that decides whether a posting may proceed.
/// </summary>
/// <param name="BatchCode">The batch being posted, for the message.</param>
/// <param name="CurrencyCode">The company base currency, for the message.</param>
/// <param name="CurrencyDecimals">
/// How many decimal places the currency has. The balance check rounds to this before comparing,
/// so a journal is judged on the figures that will actually be stored.
/// </param>
/// <param name="ResolvePeriod">What the fiscal calendar says about a date.</param>
/// <param name="PostingWindowFrom">Earliest date this user may post to, or null for no limit.</param>
/// <param name="PostingWindowTo">Latest date this user may post to, or null for no limit.</param>
/// <param name="MandatoryDimensions">Dimensions every entry must carry a value for.</param>
/// <param name="HeldOverridePermissions">
/// Override permissions the caller holds. A block whose override the caller holds is reported as
/// a warning instead, and recorded as an override in the audit log.
/// </param>
/// <param name="IsManualEntry">
/// True when a person is posting by hand. Modules posting their own documents set this false, so
/// the inventory module can write to the inventory account that a clerk may not.
/// </param>
public sealed record PostingEnvironment(
    string BatchCode,
    string CurrencyCode,
    Func<DateOnly, PeriodStatus> ResolvePeriod,
    int CurrencyDecimals = 2,
    DateOnly? PostingWindowFrom = null,
    DateOnly? PostingWindowTo = null,
    IReadOnlyList<MandatoryDimensionView>? MandatoryDimensions = null,
    IReadOnlySet<string>? HeldOverridePermissions = null,
    bool IsManualEntry = true)
{
    /// <summary>Dimensions every entry must carry a value for.</summary>
    public IReadOnlyList<MandatoryDimensionView> Mandatory => MandatoryDimensions ?? [];

    /// <summary>Whether the caller can push past a given block.</summary>
    /// <param name="permission">The override permission named on the message.</param>
    public bool CanOverride(string? permission)
        => permission is not null
           && HeldOverridePermissions is not null
           && HeldOverridePermissions.Contains(permission);
}
