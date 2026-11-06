using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How the cases of a bucket ended: <see cref="Cases" /> with an outcome, of them <see cref="Targets" />, <see cref="Stops" /> and
///     <see cref="Expired" />; the share of targets among all of them with its 95 % Wilson interval, and the expectancy, the mean result in R
///     after fees, with its 95 % interval and the <see cref="Badge" /> by it. Under 30 cases, «мало данных», neither interval, the share nor the
///     badge. Shares are fractions.
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
    decimal? ExpectancyHigh)
{
    public IdeaBadge? Badge => ExpectancyLow switch
    {
        null => null,
        > 0 => IdeaBadge.Up,
        _ => ExpectancyHigh < 0 ? IdeaBadge.Down : IdeaBadge.Flat
    };
}