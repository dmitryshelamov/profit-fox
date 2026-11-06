using System.Globalization;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A row of the table by own data: the checked cases whose bucket had 30 own cases and more, or fewer with <see cref="FewOwnCases" />, with
///     the columns of a row by badge and the Brier score of their promised probability. Numbers are decimal strings.
/// </summary>
public sealed record CalibrationOwnRowResponse(
    bool FewOwnCases,
    string Brier,
    string Promised,
    int Cases,
    string TargetShare,
    string Expectancy,
    string? ExpectancyLow,
    string? ExpectancyHigh)
{
    public static CalibrationOwnRowResponse Of(CalibrationOwnRow own)
    {
        var row = CalibrationRowResponse.Of(own.Row);

        return new CalibrationOwnRowResponse(
            own.FewOwnCases,
            own.Brier.ToString(CultureInfo.InvariantCulture),
            row.Promised,
            row.Cases,
            row.TargetShare,
            row.Expectancy,
            row.ExpectancyLow,
            row.ExpectancyHigh);
    }
}