using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The statistics of a bucket: its cases with an outcome, of them the targets, the stops and the expired ones, the share of targets among
///     all of them and the expectancy in R of the model with their 95 % intervals and the badge by the expectancy, <c>up</c>, <c>flat</c> or
///     <c>down</c> (no share, intervals or badge with <see cref="EnoughData" /> false, «мало данных»: the parent has fewer than 30 cases). Beside
///     them the own share and expectancy of the bucket with their intervals (none under 30 own cases), the weight of the parent in the
///     expectancy of the model and <see cref="FewOwnCases" />, «мало своих данных», and the expectancy of the model without costs, over the results
///     at the prices of the plan, with its interval (none for a bucket counted before it was kept; no interval with «мало данных»). A bucket of
///     «Откат» or «Стоп-вход» also has the signals it
///     counted, the share of them that entered and the expectancy per signal. Shares are fractions as decimal strings.
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
    bool EnoughData,
    int? Signals,
    string? EntryShare,
    string? SignalExpectancy,
    string? OwnProbability,
    string? OwnProbabilityLow,
    string? OwnProbabilityHigh,
    string OwnExpectancy,
    string? OwnExpectancyLow,
    string? OwnExpectancyHigh,
    string? ParentWeight,
    bool FewOwnCases,
    string? GrossExpectancy,
    string? GrossExpectancyLow,
    string? GrossExpectancyHigh)
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
                statistics.Probability is not null,
                statistics.Signals,
                Text(statistics.EntryShare),
                Text(statistics.SignalExpectancy),
                Text(statistics.OwnProbability),
                Text(statistics.OwnProbabilityLow),
                Text(statistics.OwnProbabilityHigh),
                Text(statistics.OwnExpectancy)!,
                Text(statistics.OwnExpectancyLow),
                Text(statistics.OwnExpectancyHigh),
                Text(statistics.ParentWeight),
                statistics.FewOwnCases,
                Text(statistics.GrossExpectancy),
                Text(statistics.GrossExpectancyLow),
                Text(statistics.GrossExpectancyHigh));
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}