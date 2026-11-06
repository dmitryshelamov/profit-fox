using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A row of a table of calibration: the cases promised from <see cref="From" /> up to <see cref="To" /> (none for an open end) or with a
///     <see cref="Badge" /> (<c>up</c>, <c>flat</c>, <c>down</c>), the mean of what they were promised, the shares of their outcomes, the 95 %
///     Wilson interval of the share of targets and their mean result in R with its 95 % interval (no intervals under 30 cases). Numbers are
///     decimal strings.
/// </summary>
public sealed record CalibrationRowResponse(
    string? From,
    string? To,
    string? Badge,
    string Promised,
    int Cases,
    string TargetShare,
    string StopShare,
    string ExpiredShare,
    string? ProbabilityLow,
    string? ProbabilityHigh,
    string Expectancy,
    string? ExpectancyLow,
    string? ExpectancyHigh)
{
    public static CalibrationRowResponse Of(CalibrationRow row)
    {
        BucketStatistics outcome = row.Outcome;

        return new CalibrationRowResponse(
            Text(row.From),
            Text(row.To),
            row.Badge?.ToCode(),
            Text(row.Promised)!,
            outcome.Cases,
            Text(row.TargetShare)!,
            Text(row.StopShare)!,
            Text(row.ExpiredShare)!,
            Text(outcome.ProbabilityLow),
            Text(outcome.ProbabilityHigh),
            Text(outcome.Expectancy)!,
            Text(outcome.ExpectancyLow),
            Text(outcome.ExpectancyHigh));
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}