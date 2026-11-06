using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A row of a table of calibration: the checked cases promised from <see cref="From" /> up to <see cref="To" /> (none for an open end), or
///     with a <see cref="Badge" />; the mean of what they were promised and how they ended, by the rules of <see cref="BucketStatistics" />, with
///     the shares of their outcomes whatever their number.
/// </summary>
public sealed record CalibrationRow(decimal? From, decimal? To, IdeaBadge? Badge, decimal Promised, BucketStatistics Outcome)
{
    public decimal TargetShare => (decimal)Outcome.Targets / Outcome.Cases;

    public decimal StopShare => (decimal)Outcome.Stops / Outcome.Cases;

    public decimal ExpiredShare => (decimal)Outcome.Expired / Outcome.Cases;
}