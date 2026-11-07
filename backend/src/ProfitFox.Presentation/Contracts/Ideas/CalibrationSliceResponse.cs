using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A row of a breakdown of the calibration: the code of its <see cref="Interval" /> (<c>5</c>, <c>15</c>, <c>60</c>, <c>240</c>), of its
///     <see cref="Type" /> or of its <see cref="Feature" /> (<c>day_drop</c>, <c>btc_drop</c>, <c>high_funding</c>) with whether its cases have it
///     (<see cref="Present" />), the others none; the row of all its checked cases, the row of those promised ▲, none without one, and the old
///     model against the new one on the cases of the row both check.
/// </summary>
public sealed record CalibrationSliceResponse(
    string? Interval,
    string? Type,
    CalibrationRowResponse Total,
    CalibrationRowResponse? Up,
    CalibrationComparisonResponse Comparison,
    string? Feature,
    bool? Present)
{
    public static CalibrationSliceResponse Of(CalibrationSlice slice)
    {
        return new CalibrationSliceResponse(
            slice.Interval?.ToCode(),
            slice.Type?.ToCode(),
            CalibrationRowResponse.Of(slice.Total),
            slice.Up is null ? null : CalibrationRowResponse.Of(slice.Up),
            CalibrationComparisonResponse.Of(slice.Comparison),
            slice.Feature?.ToCode(),
            slice.Present);
    }
}