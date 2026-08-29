using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Parties;
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

/// <summary>
/// What the posting engine needs to know about a customer or vendor a line posts to.
/// </summary>
/// <param name="Id">The party key.</param>
/// <param name="No">The party number.</param>
/// <param name="Name">The party name.</param>
/// <param name="Kind">Which subsidiary ledger it belongs to.</param>
/// <param name="IsBlocked">Whether it has been withdrawn from use.</param>
/// <param name="PaymentTermsDays">How many days after the posting date payment falls due.</param>
/// <param name="ControlAccountNo">
/// The control account this party posts to, already resolved from the party's own override or the
/// company default.
/// </param>
/// <param name="CreditLimit">The most the party may owe, or zero for no limit.</param>
/// <param name="Balance">What the party owed before this posting.</param>
/// <param name="TaxRegistrationNo">
/// Their tax registration number, copied onto any tax entry the line produces. An audited return
/// has to show who a figure was charged to.
/// </param>
public sealed record PostingPartyView(
    Guid Id,
    string No,
    string Name,
    PartyKind Kind,
    bool IsBlocked,
    int PaymentTermsDays,
    string ControlAccountNo,
    decimal CreditLimit = 0m,
    decimal Balance = 0m,
    string? TaxRegistrationNo = null)
{
    /// <summary>Whether an entry may land on this party at all.</summary>
    public bool IsPostable => !IsBlocked;

    /// <summary>Builds a view from a party entity.</summary>
    /// <param name="party">The customer or vendor.</param>
    /// <param name="defaultControlAccountNo">The company control account, used when the party names none.</param>
    public static PostingPartyView From(Party party, string defaultControlAccountNo)
    {
        ArgumentNullException.ThrowIfNull(party);

        return new PostingPartyView(
            party.Id,
            party.No,
            party.Name,
            party.Kind,
            party.IsBlocked,
            party.PaymentTermsDays,
            party.ControlAccountNo ?? defaultControlAccountNo,
            party.CreditLimit,
            party.Balance,
            party.TaxRegistrationNo);
    }
}

/// <summary>
/// What the posting engine needs to know about the tax on a line.
/// </summary>
/// <param name="Id">The tax code key.</param>
/// <param name="Code">The code, for example <c>VAT15</c>.</param>
/// <param name="Kind">How the code behaves.</param>
/// <param name="Direction">Whether this is tax charged out or tax paid in.</param>
/// <param name="Percentage">
/// The rate in force on the line's posting date, already resolved. Held here rather than looked up
/// later so a rate change can never restate a figure that has been posted.
/// </param>
/// <param name="Account">The account the tax lands on, or null when the code names none.</param>
/// <param name="TaxIncludedInAmount">
/// Whether the line's amount already contains the tax. A wholesaler enters the net and the tax is
/// added; a shop enters the shelf price and the tax comes out of it. Both happen in one company,
/// so the line says which rather than the company deciding once for everybody.
/// </param>
public sealed record PostingTaxView(
    Guid Id,
    string Code,
    Tax.TaxKind Kind,
    Tax.TaxDirection Direction,
    decimal Percentage,
    string? Account,
    bool TaxIncludedInAmount = false);

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
/// <param name="Party">
/// The customer or vendor this line posts to, when it posts to one rather than straight to an
/// account.
/// </param>
/// <param name="ExternalDocumentNo">
/// The other side's own reference, such as the number printed on the vendor's invoice. Carried on
/// the party entry, where it is what somebody searches by when a supplier telephones.
/// </param>
/// <param name="Tax">The tax to post beside this line, or null for a line carrying none.</param>
/// <param name="BranchId">
/// Which branch the entry belongs to, or null to take the branch the caller is signed in to.
/// <para>
/// Stated per line because one document can belong to several. A payroll run covering somebody
/// who transferred mid-month charges part of their wage to each branch, and a run posted from
/// head office with every entry taking the ambient branch would charge the whole company's wages
/// to head office and make cost per branch unanswerable.
/// </para>
/// </param>
/// <remarks>
/// <para>
/// A line naming a party still produces an ordinary general ledger entry -- on the party's control
/// account -- and a subsidiary ledger entry beside it, in the same transaction. That is what makes
/// the control account and the customer ledger incapable of disagreeing, and it is why the control
/// accounts ship with direct posting switched off: this is the only road to them.
/// </para>
/// <para>
/// <see cref="Amount"/> is always in the company's own currency, converted before it ever reaches
/// here. Every rule that follows -- the balance check above all -- is arithmetic on one currency,
/// and a validator that had to convert would be a validator that could refuse a document for
/// want of a rate, which is not its job and not where anybody would look for the reason.
/// <see cref="CurrencyCode"/> and the two beside it are what the document was written in, carried
/// through to be recorded rather than to be calculated with.
/// </para>
/// </remarks>
/// <param name="DimensionSetId">
/// The stored combination this line's entries point at, or null to take the document's. A line
/// naming its own department wants its own set; the rest of the document is unaffected.
/// </param>
/// <param name="CurrencyCode">What the document was written in, or null for the company's own.</param>
/// <param name="AmountInCurrency">The amount as written, before conversion.</param>
/// <param name="ExchangeRate">
/// What one unit of the currency was worth, recorded so the entry can be explained later without
/// anybody having to trust that the rate table has not been edited since.
/// </param>
public sealed record PostingLineView(
    int LineNo,
    DateOnly PostingDate,
    decimal Amount,
    PostingAccountView? Account,
    PostingAccountView? BalancingAccount = null,
    DimensionCombination Dimensions = default,
    string? DocumentNo = null,
    string? Description = null,
    PostingPartyView? Party = null,
    string? ExternalDocumentNo = null,
    PostingTaxView? Tax = null,
    Guid? BranchId = null,
    Guid? DimensionSetId = null,
    string? CurrencyCode = null,
    decimal? AmountInCurrency = null,
    decimal? ExchangeRate = null);

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
/// <param name="TaxAccountsByNo">
/// The accounts a generated tax line may land on. Supplied separately because tax accounts are
/// control accounts nobody names by hand, so they never appear among the lines being posted.
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
    bool IsManualEntry = true,
    IReadOnlyDictionary<string, PostingAccountView>? TaxAccountsByNo = null)
{
    /// <summary>Dimensions every entry must carry a value for.</summary>
    public IReadOnlyList<MandatoryDimensionView> Mandatory => MandatoryDimensions ?? [];

    /// <summary>
    /// The accounts tax lands on, keyed by number.
    /// </summary>
    /// <remarks>
    /// Resolved up front because a tax line is generated during posting rather than named by the
    /// user, and the engine has no way to look an account up once it is running.
    /// </remarks>
    public IReadOnlyDictionary<string, PostingAccountView> TaxAccounts
        => TaxAccountsByNo ?? EmptyAccounts;

    private static readonly Dictionary<string, PostingAccountView> EmptyAccounts
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the caller can push past a given block.</summary>
    /// <param name="permission">The override permission named on the message.</param>
    public bool CanOverride(string? permission)
        => permission is not null
           && HeldOverridePermissions is not null
           && HeldOverridePermissions.Contains(permission);
}
