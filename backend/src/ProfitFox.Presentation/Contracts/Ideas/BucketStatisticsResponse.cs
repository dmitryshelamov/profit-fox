using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The statistics of a bucket: its cases with an outcome, of them the targets, the stops and the expired ones, the share of targets among
///     all of them with its 95 % Wilson interval, the expectancy in R with its 95 % interval and the badge by it, <c>up</c>, <c>flat</c> or
///     <c>down</c> (no share, intervals or badge with <see cref="EnoughData" /> false, «мало данных»). Shares are fractions as decimal strings.
/// </summary>
public sealed record BucketStatisticsResponse(
    int Cases,
    int Targets,
    int Stops,
    int Expired,
    string? Probability,
    string? ProbabilityLow,
    string? ProbabilityHigh,
    string Expectancy,
    string? ExpectancyLow,
    string? ExpectancyHigh,
    string? Badge,
    bool EnoughData)
{
    public static BucketStatisticsResponse? Of(BucketStatistics? statistics)
    {
        return statistics is null
            ? null
            : new BucketStatisticsResponse(
                statistics.Cases,
                statistics.Targets,
                statistics.Stops,
                statistics.Expired,
                Text(statistics.Probability),
                Text(statistics.ProbabilityLow),
                Text(statistics.ProbabilityHigh),
                Text(statistics.Expectancy)!,
                Text(statistics.ExpectancyLow),
                Text(statistics.ExpectancyHigh),
                statistics.Badge?.ToCode(),
                statistics.Cases >= BucketStatisticsCalculator.MinimumCases);
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}