namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The window of the calibration, its category and its scope; no window takes 90 days, no category all of them, no scope all cases.</summary>
public sealed class IdeaCalibrationParameters
{
    /// <summary><c>90</c> or <c>30</c>.</summary>
    public string? Days { get; init; }

    /// <summary><c>linear</c> or <c>spot</c>.</summary>
    public string? Category { get; init; }

    /// <summary><c>all</c> or <c>fresh</c>, the cases after the rules changed.</summary>
    public string? Scope { get; init; }
}