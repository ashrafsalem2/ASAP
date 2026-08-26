using ASAP.Platform.Kernel.Results;

namespace ASAP.Platform.Kernel.Numbering;

/// <summary>
/// Issues document numbers from the configured series.
/// </summary>
/// <remarks>
/// Modules and extensions ask for a number rather than composing one, so numbering policy stays
/// with the administrator who owns it. Whether an allocation can leave a gap is decided by the
/// series, not by the caller.
/// </remarks>
public interface INumberSeriesService
{
    /// <summary>
    /// Takes the next number from a series.
    /// </summary>
    /// <param name="seriesCode">The series code, for example <c>SALES-INV</c>.</param>
    /// <param name="documentDate">
    /// The document date. Chooses which dated line issues the number, and is checked against the
    /// last one issued when the series enforces date order.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The issued number, or a failure when the series is unknown, inactive, exhausted, or when
    /// the document date runs backwards against a series that forbids it. A gapless series
    /// allocates inside the caller transaction, so abandoning it returns the number.
    /// </returns>
    Task<Result<string>> NextAsync(
        string seriesCode,
        DateOnly documentDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports what the next number would be without taking it, for showing on a document being
    /// drafted.
    /// </summary>
    /// <param name="seriesCode">The series code.</param>
    /// <param name="documentDate">The document date.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The number that would be issued. It is not reserved, so on a busy series another user may
    /// take it first. Never show this as final on a posted document.
    /// </returns>
    Task<Result<string>> PeekAsync(
        string seriesCode,
        DateOnly documentDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a number a user typed by hand.
    /// </summary>
    /// <param name="seriesCode">The series the number should belong to.</param>
    /// <param name="number">The number as typed.</param>
    /// <param name="documentDate">The document date.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A failure when the series forbids manual entry, when the number is already used, or when
    /// it falls outside the range the series may issue.
    /// </returns>
    Task<Result> ValidateManualAsync(
        string seriesCode,
        string number,
        DateOnly documentDate,
        CancellationToken cancellationToken = default);
}
