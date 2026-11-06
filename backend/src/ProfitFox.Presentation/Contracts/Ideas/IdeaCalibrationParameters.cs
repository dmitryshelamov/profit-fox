namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The window of the calibration and its category; no window takes 90 days, no category all of them.</summary>
public sealed class IdeaCalibrationParameters
{
    /// <summary><c>90</c> or <c>30</c>.</summary>
    public string? Days { get; init; }

    /// <summary><c>linear</c> or <c>spot</c>.</summary>
    public string? Category { get; init; }
}