using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How the cases of a bucket ended: <see cref="Cases" /> with an outcome, of them <see cref="Targets" />, <see cref="Stops" /> and
///     <see cref="Expired" />; the share of targets among all of them with its 95 % interval, and the expectancy, the mean result in R
///     after fees, with its 95 % interval and the <see cref="Badge" /> by it. The numbers of a bucket of the scanner are those of the model
///     (decision 3 of honest-idea-intervals), its own ones go beside them; without a parent of 30 cases, «мало данных», neither interval, the
///     share nor the badge, and the expectancy is the own one. Shares are fractions. A bucket of «Отката» or «Стоп-входа» also has the <see cref="Signals" /> it counted, entered or not: the share
///     of them that entered and the expectancy per signal, a signal without an entry as 0 R (requirement "Ожидание на сигнал").
/// </summary>
public sealed record BucketStatistics(
    int Cases,
    int Targets,
    int Stops,
    int Expired,
    decimal? Probability,
    decimal? ProbabilityLow,
    decimal? ProbabilityHigh,
    decimal Expectancy,
    decimal? ExpectancyLow,
    decimal? ExpectancyHigh,
    int? Signals = null)
{
    /// <summary>The own share of targets of the bucket, with the clustering of its cases by the hour and without the parent; none under 30 own cases.</summary>
    public decimal? OwnProbability { get; init; }

    public decimal? OwnProbabilityLow { get; init; }

    public decimal? OwnProbabilityHigh { get; init; }

    /// <summary>The own mean result of the bucket; its interval, with the clustering, none under 30 own cases.</summary>
    public decimal OwnExpectancy { get; init; }

    public decimal? OwnExpectancyLow { get; init; }

    public decimal? OwnExpectancyHigh { get; init; }

    /// <summary>The weight of the parent in the expectancy of the model, 1 − B (decision 2 of honest-idea-intervals).</summary>
    public decimal? ParentWeight { get; init; }

    /// <summary>
    ///     The expectancy without costs (decision 3 of add-own-idea-numbers): the model run a second time over the results at the prices of the
    ///     plan, its interval none for «мало данных»; none for a bucket stored before it was counted.
    /// </summary>
    public decimal? GrossExpectancy { get; init; }

    public decimal? GrossExpectancyLow { get; init; }

    public decimal? GrossExpectancyHigh { get; init; }

    /// <summary>«Мало своих данных»: fewer than 30 own cases, the numbers of the model by the parent.</summary>
    public bool FewOwnCases { get; init; }

    public decimal? EntryShare => Signals is { } signals ? (decimal)Cases / signals : null;

    public decimal? SignalExpectancy => Signals is { } signals ? Expectancy * Cases / signals : null;

    public IdeaBadge? Badge => IdeaBadges.Of(ExpectancyLow, ExpectancyHigh);
}