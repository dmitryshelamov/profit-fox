using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     What the preview of a strategy is asked for: the symbol of a pair, a timeframe (<c>1h</c>, <c>4h</c>, <c>1d</c>) and
///     the graph of the strategy as written, with its signal nodes.
/// </summary>
public sealed record PreviewStrategyRequest(string? Instrument, string? Timeframe, JsonElement? Strategy);
