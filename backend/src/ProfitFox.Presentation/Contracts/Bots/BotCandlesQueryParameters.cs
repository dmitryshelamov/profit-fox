using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.Bots;

public sealed class BotCandlesQueryParameters
{
    /// <summary>A Bybit interval; without it the interval of the bot.</summary>
    public string? Interval { get; init; }

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string From { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string To { get; init; } = string.Empty;
}