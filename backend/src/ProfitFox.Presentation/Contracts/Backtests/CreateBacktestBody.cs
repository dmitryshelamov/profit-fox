using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed class CreateBacktestBody
{
    /// <summary>ISO 8601 with an offset or <c>Z</c>.</summary>
    [Required]
    public string From { get; init; } = string.Empty;

    /// <summary>ISO 8601 with an offset or <c>Z</c>; the end is not included.</summary>
    [Required]
    public string To { get; init; } = string.Empty;

    /// <summary>A value or a range <c>{ "min", "max", "step" }</c> per parameter; a parameter left out takes the value of the bot.</summary>
    public JsonElement? Parameters { get; init; }

    /// <summary><c>grid</c>, the only search mode for now.</summary>
    public string? SearchMode { get; init; }

    public SimulationBody? Simulation { get; init; }
}