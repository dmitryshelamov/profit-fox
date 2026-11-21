using System.Text.Json;

namespace ProfitFox.Application.Preview;

/// <summary>
///     What the preview is asked for, as the request gave it: the symbol of a pair, a timeframe (<c>1h</c>, <c>4h</c>,
///     <c>1d</c>) and the signal graph in the format of spec strategy-graph. Nothing is checked yet.
/// </summary>
public sealed record PreviewQuery(string? Instrument, string? Timeframe, JsonElement? Signal);
