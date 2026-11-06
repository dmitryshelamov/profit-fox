using System.Globalization;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The old model, the own numbers of a bucket from 30 own cases without clustering or shrinking, against the new one, on the cases both check.</summary>
public sealed record CalibrationComparisonResponse(CalibrationModelResponse Old, CalibrationModelResponse New)
{
    public static CalibrationComparisonResponse Of(CalibrationComparison comparison)
    {
        return new CalibrationComparisonResponse(CalibrationModelResponse.Of(comparison.Old), CalibrationModelResponse.Of(comparison.New));
    }
}

/// <summary>How a model did: the cases compared, the Brier score of their promised probability, and the row of those it promised ▲, none without one.</summary>
public sealed record CalibrationModelResponse(int Cases, string? Brier, CalibrationRowResponse? Up)
{
    public static CalibrationModelResponse Of(CalibrationModel model)
    {
        return new CalibrationModelResponse(model.Cases, model.Brier?.ToString(CultureInfo.InvariantCulture), model.Up is null ? null : CalibrationRowResponse.Of(model.Up));
    }
}