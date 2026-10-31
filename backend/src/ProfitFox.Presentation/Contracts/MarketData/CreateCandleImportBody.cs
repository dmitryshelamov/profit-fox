using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.MarketData;

public sealed class CreateCandleImportBody
{
    // [Required] rather than C# `required`: a missing property is then reported under its own name, not under `$`.
    [Required]
    public string Category { get; init; } = string.Empty;

    [Required]
    public string Symbol { get; init; } = string.Empty;

    [Required]
    public string Interval { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string From { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>; defaults to now.</summary>
    public string? To { get; init; }
}
