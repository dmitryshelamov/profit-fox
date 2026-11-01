using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Bots;

/// <summary>Settings of a bot; a change ignores <see cref="StrategyId" />, because the strategy of a bot never changes.</summary>
public sealed class BotBody
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public Guid? StrategyId { get; init; }

    /// <summary>Values of strategy parameters, such as <c>{ "fast": 10 }</c>; parameters without a value take the strategy default.</summary>
    public JsonElement? ParameterValues { get; init; }

    [Required]
    public string Category { get; init; } = string.Empty;

    [Required]
    public string Symbol { get; init; } = string.Empty;

    [Required]
    public string Interval { get; init; } = string.Empty;

    /// <summary>In the quote asset.</summary>
    [Required]
    public decimal? Balance { get; init; }
}