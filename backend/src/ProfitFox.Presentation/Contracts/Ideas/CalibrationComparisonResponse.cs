using System.Globalization;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The old model, the own numbers of a bucket from 30 own cases without clustering or shrinking, against the new one and the model with
///     features, on the cases all three check, and the Brier score of the model with features less that of the new one; a report computed before
///     the model with features has neither.
/// </summary>
public sealed record CalibrationComparisonResponse(
    CalibrationModelResponse Old,
    CalibrationModelResponse New,
    CalibrationModelResponse? Features,
    CalibrationDifferenceResponse? Difference)
{
    public static CalibrationComparisonResponse Of(CalibrationComparison comparison)
    {
        return new CalibrationComparisonResponse(
            CalibrationModelResponse.Of(comparison.Old),
            CalibrationModelResponse.Of(comparison.New),
            comparison.Features is null ? null : CalibrationModelResponse.Of(comparison.Features),
            comparison.Difference is not { } difference
                ? null
                : new CalibrationDifferenceResponse(
                    difference.Mean.ToString(CultureInfo.InvariantCulture),
                    difference.Low?.ToString(CultureInfo.InvariantCulture),
                    difference.High?.ToString(CultureInfo.InvariantCulture)));
    }
}

/// <summary>The paired difference of the Brier scores, features less new, with its 95 % interval, none under 30 cases.</summary>
public sealed record CalibrationDifferenceResponse(string Mean, string? Low, string? High);

/// <summary>How a model did: the cases compared, the Brier score of their promised probability, and the row of those it promised ▲, none without one.</summary>
public sealed record CalibrationModelResponse(int Cases, string? Brier, CalibrationRowResponse? Up)
{
    public static CalibrationModelResponse Of(CalibrationModel model)
    {
        return new CalibrationModelResponse(model.Cases, model.Brier?.ToString(CultureInfo.InvariantCulture), model.Up is null ? null : CalibrationRowResponse.Of(model.Up));
    }
}