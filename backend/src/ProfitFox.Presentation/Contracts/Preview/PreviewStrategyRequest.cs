using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     What the preview of a strategy is asked for: the symbol of a pair, a timeframe (<c>1h</c>, <c>4h</c>, <c>1d</c>), the
///     graph of the strategy as written, with its signal nodes, and, when given, the fees of a taker and of a maker as
///     fractions of the amount of a fill (by default the base rates of Bybit, 0.00055 and 0.0002).
/// </summary>
public sealed record PreviewStrategyRequest(string? Instrument, string? Timeframe, JsonElement? Strategy, double? TakerFee = null, double? MakerFee = null);
