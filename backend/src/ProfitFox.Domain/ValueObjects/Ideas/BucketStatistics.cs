namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How the cases of a bucket ended: <see cref="Cases" /> with an outcome, of them <see cref="Expired" />; the share of targets among targets
///     and stops with its 95 % Wilson interval (none under 30 cases, «мало данных»), the baseline 1 / (1 + R:R) and the expectancy in R after
///     fees. Shares are fractions.
/// </summary>
public sealed record BucketStatistics(
    int Cases,
    int Expired,
    decimal? Probability,
    decimal? ProbabilityLow,
    decimal? ProbabilityHigh,
    decimal Baseline,
    decimal Expectancy)
{
    public bool IsAboveBaseline => ProbabilityLow > Baseline;
}