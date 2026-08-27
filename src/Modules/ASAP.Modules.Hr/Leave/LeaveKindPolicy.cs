namespace ASAP.Modules.Hr.Leave;

/// <summary>
/// One band of a leave's pay: up to this many days in the year, this fraction of the wage.
/// </summary>
/// <param name="UpToDays">
/// The cumulative day this band runs to, counted across the leave year, or null for the band that
/// runs to the end.
/// </param>
/// <param name="PaidFraction">What fraction of the wage those days carry. One is full pay.</param>
public readonly record struct LeavePayBand(decimal? UpToDays, decimal PaidFraction);

/// <summary>
/// What one kind of leave costs and what it draws on.
/// </summary>
/// <remarks>
/// <para>
/// Data, like the end-of-service and accrual bands, and for the same reason: the numbers here are
/// the Saudi Labour Law's and another country's are different numbers rather than different code.
/// </para>
/// <para>
/// The pay bands are cumulative across the leave year, exactly as tax bands are cumulative across
/// income. Thirty days of sickness in March and another ten in September is forty days of sick
/// leave, not two separate first-thirty-days.
/// </para>
/// </remarks>
/// <param name="Kind">Which leave this describes.</param>
/// <param name="DrawsOnAnnualBalance">
/// Whether taking it reduces accrued annual leave. Only annual leave does; a system that let
/// sickness eat somebody's holiday would be wrong and would also be illegal here.
/// </param>
/// <param name="PayBands">What fraction of the wage the days carry, by cumulative day.</param>
/// <param name="OncePerService">
/// Whether the entitlement is granted once for a whole period of employment rather than each
/// year. Hajj leave is; nothing else here is.
/// </param>
public sealed record LeaveKindPolicy(
    LeaveKind Kind,
    bool DrawsOnAnnualBalance,
    IReadOnlyList<LeavePayBand> PayBands,
    bool OncePerService = false)
{
    /// <summary>
    /// What the Saudi Labour Law says, which is what ships.
    /// </summary>
    /// <remarks>
    /// Sick leave is article 117: the first thirty days at full pay, the next sixty at three
    /// quarters, the thirty after that unpaid. Maternity is article 151, ten weeks. Marriage and
    /// bereavement are article 113, five days each. Hajj is article 114, ten to fifteen days once
    /// in a period of service — the fifteen is what is shipped, because granting the maximum and
    /// letting somebody ask for less is the way round that does not underpay anybody by default.
    /// </remarks>
    public static IReadOnlyDictionary<LeaveKind, LeaveKindPolicy> Saudi { get; } =
        new Dictionary<LeaveKind, LeaveKindPolicy>
        {
            [LeaveKind.Annual] = new(LeaveKind.Annual, true, [new LeavePayBand(null, 1m)]),

            [LeaveKind.Sick] = new(
                LeaveKind.Sick,
                false,
                [
                    new LeavePayBand(30m, 1m),
                    new LeavePayBand(90m, 0.75m),
                    new LeavePayBand(null, 0m),
                ]),

            [LeaveKind.Unpaid] = new(LeaveKind.Unpaid, false, [new LeavePayBand(null, 0m)]),

            [LeaveKind.Maternity] = new(
                LeaveKind.Maternity,
                false,
                [new LeavePayBand(70m, 1m), new LeavePayBand(null, 0m)]),

            [LeaveKind.Hajj] = new(
                LeaveKind.Hajj,
                false,
                [new LeavePayBand(15m, 1m), new LeavePayBand(null, 0m)],
                OncePerService: true),

            [LeaveKind.Marriage] = new(
                LeaveKind.Marriage,
                false,
                [new LeavePayBand(5m, 1m), new LeavePayBand(null, 0m)]),

            [LeaveKind.Bereavement] = new(
                LeaveKind.Bereavement,
                false,
                [new LeavePayBand(5m, 1m), new LeavePayBand(null, 0m)]),

            [LeaveKind.Examination] = new(
                LeaveKind.Examination,
                false,
                [new LeavePayBand(null, 1m)]),
        };

    /// <summary>The policy for a kind, falling back to full pay drawing on nothing.</summary>
    /// <param name="kind">The kind of leave.</param>
    /// <returns>Its policy.</returns>
    /// <remarks>
    /// An unknown kind is paid in full rather than refused. A new kind added by an extension and
    /// not yet given a policy should not quietly stop paying somebody.
    /// </remarks>
    public static LeaveKindPolicy For(LeaveKind kind)
        => Saudi.TryGetValue(kind, out var policy)
            ? policy
            : new LeaveKindPolicy(kind, false, [new LeavePayBand(null, 1m)]);
}
