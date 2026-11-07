using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
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
            Total = report.Total is null ? null : FromDomain(report.Total),
            Owns = [.. report.Owns.Select(own => new CalibrationOwnRowDb { FewOwnCases = own.FewOwnCases, Row = FromDomain(own.Row), Brier = own.Brier })],
            Comparison = report.Comparison is not { } comparison ? null : FromDomain(comparison),
            Intervals = [.. report.Intervals.Select(FromDomain)],
            Types = [.. report.Types.Select(FromDomain)]
        };
    }

    public static CalibrationReport ToDomain(CalibrationReportDb row, DateTimeOffset computedAt, int days, MarketCategory? category, CalibrationScope scope)
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
            row.Total is null ? null : ToDomain(row.Total))
        {
            Scope = scope,
            Owns = [.. row.Owns.Select(own => new CalibrationOwnRow(own.FewOwnCases, ToDomain(own.Row), own.Brier))],
            Comparison = row.Comparison is not { } comparison ? null : ToDomain(comparison),
            Intervals = [.. row.Intervals.Select(ToDomain)],
            Types = [.. row.Types.Select(ToDomain)]
        };
    }

    private static CalibrationSliceDb FromDomain(CalibrationSlice slice)
    {
        return new CalibrationSliceDb
        {
            Interval = slice.Interval?.ToCode(),
            Type = slice.Type?.ToCode(),
            Total = FromDomain(slice.Total),
            Up = slice.Up is null ? null : FromDomain(slice.Up),
            Comparison = FromDomain(slice.Comparison)
        };
    }

    private static CalibrationSlice ToDomain(CalibrationSliceDb slice)
    {
        return new CalibrationSlice(
            IntervalOf(slice.Interval),
            TypeOf(slice.Type),
            ToDomain(slice.Total),
            slice.Up is null ? null : ToDomain(slice.Up),
            ToDomain(slice.Comparison));
    }

    private static Interval? IntervalOf(string? code)
    {
        return code is null ? null : MarketDataCodes.TryParseInterval(code, out Interval interval) ? interval : throw Unknown("interval", code);
    }

    private static IdeaType? TypeOf(string? code)
    {
        return code is null ? null : IdeaCodes.TryParseType(code, out IdeaType type) ? type : throw Unknown("type", code);
    }

    private static InvalidOperationException Unknown(string what, string code)
    {
        return new InvalidOperationException($"Unknown {what} code '{code}' in storage.");
    }

    private static CalibrationComparisonDb FromDomain(CalibrationComparison comparison)
    {
        return new CalibrationComparisonDb
        {
            Old = FromDomain(comparison.Old),
            New = FromDomain(comparison.New),
            Features = comparison.Features is null ? null : FromDomain(comparison.Features),
            Difference = comparison.Difference is not { } difference
                ? null
                : new CalibrationDifferenceDb { Mean = difference.Mean, Low = difference.Low, High = difference.High }
        };
    }

    private static CalibrationComparison ToDomain(CalibrationComparisonDb comparison)
    {
        return new CalibrationComparison(
            ToDomain(comparison.Old),
            ToDomain(comparison.New),
            comparison.Features is null ? null : ToDomain(comparison.Features),
            comparison.Difference is not { } difference ? null : new CalibrationDifference(difference.Mean, difference.Low, difference.High));
    }

    private static CalibrationModelDb FromDomain(CalibrationModel model)
    {
        return new CalibrationModelDb { Cases = model.Cases, Brier = model.Brier, Up = model.Up is null ? null : FromDomain(model.Up) };
    }

    private static CalibrationModel ToDomain(CalibrationModelDb model)
    {
        return new CalibrationModel(model.Cases, model.Brier, model.Up is null ? null : ToDomain(model.Up));
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