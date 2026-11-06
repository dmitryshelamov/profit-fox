using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

internal static class CalibrationReportDbConverter
{
    public static CalibrationReportDb FromDomain(CalibrationReport report)
    {
        return new CalibrationReportDb
        {
            Checked = report.Checked,
            Unchecked = report.Unchecked,
            Brier = report.Brier,
            BaseBrier = report.BaseBrier,
            Probabilities = [.. report.Probabilities.Select(FromDomain)],
            Expectancies = [.. report.Expectancies.Select(FromDomain)],
            Badges = [.. report.Badges.Select(FromDomain)],
            Total = report.Total is null ? null : FromDomain(report.Total)
        };
    }

    public static CalibrationReport ToDomain(CalibrationReportDb row, DateTimeOffset computedAt, int days, MarketCategory? category)
    {
        return new CalibrationReport(
            computedAt,
            days,
            category,
            row.Checked,
            row.Unchecked,
            row.Brier,
            row.BaseBrier,
            [.. row.Probabilities.Select(ToDomain)],
            [.. row.Expectancies.Select(ToDomain)],
            [.. row.Badges.Select(ToDomain)],
            row.Total is null ? null : ToDomain(row.Total));
    }

    private static CalibrationRowDb FromDomain(CalibrationRow row)
    {
        BucketStatistics outcome = row.Outcome;

        return new CalibrationRowDb
        {
            From = row.From,
            To = row.To,
            Badge = row.Badge?.ToCode(),
            Promised = row.Promised,
            Targets = outcome.Targets,
            Stops = outcome.Stops,
            Expired = outcome.Expired,
            Probability = outcome.Probability,
            ProbabilityLow = outcome.ProbabilityLow,
            ProbabilityHigh = outcome.ProbabilityHigh,
            Expectancy = outcome.Expectancy,
            ExpectancyLow = outcome.ExpectancyLow,
            ExpectancyHigh = outcome.ExpectancyHigh
        };
    }

    private static CalibrationRow ToDomain(CalibrationRowDb row)
    {
        IdeaBadge? badge = IdeaCodes.TryParseBadge(row.Badge ?? "", out IdeaBadge parsed) ? parsed : null;

        return new CalibrationRow(
            row.From,
            row.To,
            badge,
            row.Promised,
            new BucketStatistics(
                row.Targets + row.Stops + row.Expired,
                row.Targets,
                row.Stops,
                row.Expired,
                row.Probability,
                row.ProbabilityLow,
                row.ProbabilityHigh,
                row.Expectancy,
                row.ExpectancyLow,
                row.ExpectancyHigh));
    }
}