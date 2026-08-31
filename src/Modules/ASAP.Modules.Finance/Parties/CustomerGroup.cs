using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// A kind of customer: wholesale, retail, staff, government.
/// </summary>
/// <remarks>
/// <para>
/// Held on the party rather than in one of the modules that use it, because a group is a fact
/// about <em>who the customer is</em> rather than about any arrangement a module has with them.
/// Promotions asks whether an offer is for wholesale; Sales asks which price list a group is on.
/// Neither owns the answer, and a copy in either would be the one that went stale.
/// </para>
/// <para>
/// That is a different judgment from the price list itself, which deliberately is not on the
/// party: what a customer pays for goods is a sales arrangement, and Finance has no business
/// knowing about it. Being a wholesaler is not an arrangement -- it is what they are.
/// </para>
/// </remarks>
public sealed class CustomerGroup : CompanyEntity
{
    /// <summary>Its code, for example <c>WHOLESALE</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What sort of customer belongs in it, for whoever assigns one.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether customers may still be put in it.
    /// </summary>
    /// <remarks>
    /// Switched off rather than deleted, because offers and price lists already point at the code
    /// and history already refers to it. A group that vanished would leave both silently matching
    /// nobody, which is the kind of change that shows up as a customer being charged the wrong
    /// price and nothing explaining why.
    /// </remarks>
    public bool IsActive { get; set; } = true;
}
