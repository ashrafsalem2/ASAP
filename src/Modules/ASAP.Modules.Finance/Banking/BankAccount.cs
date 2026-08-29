using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Banking;

/// <summary>
/// A bank account the company holds, and the ledger account that stands for it.
/// </summary>
/// <remarks>
/// <para>
/// Two things that are easy to confuse and must not be. The ledger account is what the company's
/// own books say it has. The bank account is what the bank says it has. They disagree constantly
/// and legitimately — a cheque written on the last day of March clears in April, a bank charge
/// appears that nobody knew about — and reconciliation is the work of proving that every
/// difference between them is one of those, rather than a mistake or a theft.
/// </para>
/// <para>
/// So this record is deliberately thin. It holds no balance of its own: the ledger account holds
/// what the books say and the statement holds what the bank says, and a third figure kept here
/// would be a number nobody could tell you the meaning of.
/// </para>
/// </remarks>
public sealed class BankAccount : CompanyEntity
{
    /// <summary>Short stable code, for example <c>SNB-MAIN</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The bank it is held at.</summary>
    public string? BankName { get; set; }

    /// <summary>The account number as the bank states it.</summary>
    public string? AccountNo { get; set; }

    /// <summary>The IBAN, which is what a payment file actually carries.</summary>
    public string? Iban { get; set; }

    /// <summary>
    /// The general ledger account this bank account is represented by.
    /// </summary>
    /// <remarks>
    /// One ledger account per bank account, never shared. Two banks sharing one account cannot be
    /// reconciled against either statement: every unmatched entry might belong to the other one,
    /// and no amount of care afterwards can separate them.
    /// </remarks>
    public required string GlAccountNo { get; set; }

    /// <summary>
    /// What the account is held in, or null for the company's own currency.
    /// </summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Whether it may still be used.</summary>
    public bool IsActive { get; set; } = true;
}
