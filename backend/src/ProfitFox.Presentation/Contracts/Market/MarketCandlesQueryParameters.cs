using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.Market;

public sealed class MarketCandlesQueryParameters
{
    /// <summary>A Bybit interval.</summary>
    [Required]
    public string Interval { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string From { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string To { get; init; } = string.Empty;
}