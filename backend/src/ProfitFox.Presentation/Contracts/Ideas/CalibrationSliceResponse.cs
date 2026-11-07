using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A row of a breakdown of the calibration: the code of its <see cref="Interval" /> (<c>5</c>, <c>15</c>, <c>60</c>, <c>240</c>) or of its
///     <see cref="Type" />, the other none; the row of all its checked cases, the row of those promised ▲, none without one, and the old model
///     against the new one on the cases of the row both check.
/// </summary>
public sealed record CalibrationSliceResponse(string? Interval, string? Type, CalibrationRowResponse Total, CalibrationRowResponse? Up, CalibrationComparisonResponse Comparison)
{
    public static CalibrationSliceResponse Of(CalibrationSlice slice)
    {
        return new CalibrationSliceResponse(
            slice.Interval?.ToCode(),
            slice.Type?.ToCode(),
            CalibrationRowResponse.Of(slice.Total),
            slice.Up is null ? null : CalibrationRowResponse.Of(slice.Up),
            CalibrationComparisonResponse.Of(slice.Comparison));
    }
}