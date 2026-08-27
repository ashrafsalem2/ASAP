namespace ASAP.Modules.Hr.Entitlements;

/// <summary>
/// One band of an end-of-service award: how much is earned per year of service within it.
/// </summary>
/// <param name="UpToYears">
/// The end of the band, in years, or null for the band that runs to the end of service.
/// </param>
/// <param name="MonthsPerYear">
/// How many months of wage each year inside this band earns. Half a month is <c>0.5</c>.
/// </param>
public readonly record struct AwardBand(decimal? UpToYears, decimal MonthsPerYear);

/// <summary>
/// How much of a full award somebody keeps when they resign, by how long they stayed.
/// </summary>
/// <param name="UpToYears">The end of the band, or null for the band that runs to the end.</param>
/// <param name="Fraction">The share of the full award, from nought to one.</param>
public readonly record struct ResignationBand(decimal? UpToYears, decimal Fraction);

/// <summary>
/// The rules an end-of-service award is worked out by.
/// </summary>
/// <remarks>
/// <para>
/// Held as data rather than written into the calculator. Labour law differs by country and
/// changes within one; a company operating in two of them needs two policies, not two versions of
/// the software. <see cref="Saudi"/> is what ships and is what nearly every deployment here will
/// use, but it is a default and not an assumption baked into the arithmetic.
/// </para>
/// <para>
/// The bands are cumulative, in the way income tax bands are: the first five years earn at the
/// first rate whatever happens afterwards. Somebody who leaves at eight years does not have all
/// eight years revalued at the higher rate, which is the mistake that doubles a provision.
/// </para>
/// </remarks>
/// <param name="Name">What the policy is called, for reporting.</param>
/// <param name="Bands">How much each year earns, in order.</param>
/// <param name="ResignationBands">How much of it a resigner keeps, in order.</param>
/// <param name="OnBasicWageOnly">
/// Whether the award is measured on the basic wage alone or on the total including allowances.
/// Saudi law says the last wage including allowances, which is why this ships false — a policy
/// that quietly used the basic would understate every award by whatever housing is worth.
/// </param>
public sealed record EndOfServicePolicy(
    string Name,
    IReadOnlyList<AwardBand> Bands,
    IReadOnlyList<ResignationBand> ResignationBands,
    bool OnBasicWageOnly = false)
{
    /// <summary>
    /// The Saudi Labour Law award, which is what ships.
    /// </summary>
    /// <remarks>
    /// Half a month's wage for each of the first five years and a full month for each year after
    /// that (article 84). On resignation the award is reduced by tenure: nothing under two years,
    /// a third between two and five, two thirds between five and ten, and the whole award after
    /// ten (article 85). Where the employer ends the contract, the full award is due.
    /// </remarks>
    public static EndOfServicePolicy Saudi { get; } = new(
        "Saudi Labour Law",
        [
            new AwardBand(5m, 0.5m),
            new AwardBand(null, 1m),
        ],
        [
            new ResignationBand(2m, 0m),
            new ResignationBand(5m, 1m / 3m),
            new ResignationBand(10m, 2m / 3m),
            new ResignationBand(null, 1m),
        ]);
}
