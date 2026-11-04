using System.Globalization;

using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The statistics of a bucket: its cases with an outcome and the expired ones, the share of targets with its 95 % Wilson interval (none
///     with <see cref="EnoughData" /> false, «мало данных»), the baseline and the expectancy in R. Shares are fractions as decimal strings.
/// </summary>
public sealed record BucketStatisticsResponse(
    int Cases,
    int Expired,
    string? Probability,
    string? ProbabilityLow,
    string? ProbabilityHigh,
    string Baseline,
    string Expectancy,
    bool EnoughData)
{
    public static BucketStatisticsResponse? Of(BucketStatistics? statistics)
    {
        return statistics is null
            ? null
            : new BucketStatisticsResponse(
                statistics.Cases,
                statistics.Expired,
                Text(statistics.Probability),
                Text(statistics.ProbabilityLow),
                Text(statistics.ProbabilityHigh),
                Text(statistics.Baseline)!,
                Text(statistics.Expectancy)!,
                statistics.Cases >= BucketStatisticsCalculator.MinimumCases);
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}