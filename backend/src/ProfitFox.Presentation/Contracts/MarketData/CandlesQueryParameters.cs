using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.MarketData;

public sealed class CandlesQueryParameters
{
    [Required]
    public string Category { get; init; } = string.Empty;

    [Required]
    public string Symbol { get; init; } = string.Empty;

    [Required]
    public string Interval { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string From { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string To { get; init; } = string.Empty;

    public int? Limit { get; init; }
}