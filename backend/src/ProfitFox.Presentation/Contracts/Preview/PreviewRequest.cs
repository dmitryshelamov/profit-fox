using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     What the preview is asked for: the symbol of a pair, a timeframe (<c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>) and the signal
///     graph in the format of spec strategy-graph.
/// </summary>
public sealed record PreviewRequest(string? Instrument, string? Timeframe, JsonElement? Signal);
