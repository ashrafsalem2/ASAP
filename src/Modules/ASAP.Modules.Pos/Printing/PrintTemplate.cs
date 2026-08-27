using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Pos.Printing;

/// <summary>What a template is for.</summary>
public enum PrintTemplateKind
{
    /// <summary>A till receipt, printed on a narrow roll.</summary>
    Receipt = 0,

    /// <summary>A shelf or product label.</summary>
    Label = 1,

    /// <summary>The reading a till gives when its drawer is counted.</summary>
    SessionReport = 2,
}

/// <summary>
/// A layout a shop manager can edit without a developer.
/// </summary>
/// <remarks>
/// <para>
/// The reason this is data rather than code is the reason most of it gets changed: a shop adds a
/// line about returns, a tax authority wants a phrase in a particular place, a branch wants its
/// own telephone number on the bottom. None of those is worth a release, and a system where they
/// are is a system where the receipt says whatever it said on the day it shipped.
/// </para>
/// <para>
/// The width matters more than it looks. A receipt roll is a fixed number of characters across,
/// and a template written for eighty on a printer that has forty-two wraps every line in the
/// middle of a word. The width is stored so the editor can show it as it will print.
/// </para>
/// </remarks>
public sealed class PrintTemplate : CompanyEntity
{
    /// <summary>The template code, which a station names to choose it.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What it is for.</summary>
    public PrintTemplateKind Kind { get; set; } = PrintTemplateKind.Receipt;

    /// <summary>The layout itself.</summary>
    public required string Content { get; set; }

    /// <summary>
    /// How many characters wide the paper is.
    /// </summary>
    /// <remarks>
    /// Forty-two is the usual eighty-millimetre roll and thirty-two the fifty-eight. Wrong by a
    /// few and every long line wraps in the middle of a word.
    /// </remarks>
    public int WidthInCharacters { get; set; } = 42;

    /// <summary>
    /// The branch this belongs to, or null for one the whole company uses.
    /// </summary>
    /// <remarks>
    /// A branch's own template beats the company one. That is the whole reason for the field: a
    /// shop wanting its own telephone number at the bottom should not need its own installation.
    /// </remarks>
    public Guid? BranchId { get; set; }

    /// <summary>Whether this is the one used when nothing names another.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether it is still in use.</summary>
    public bool IsActive { get; set; } = true;
}
