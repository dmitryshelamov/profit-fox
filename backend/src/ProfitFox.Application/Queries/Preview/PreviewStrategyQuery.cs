using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Preview;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The preview of a strategy on one pair over exploration (spec strategy-preview), asked as the request gave it: the
///     symbol of a pair, a timeframe (<c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>), the graph of the strategy as written, with its
///     signal nodes, and the fees of a taker and of a maker as fractions, when given (spec strategy-preview «Ставки комиссии
///     в запросе»). Nothing is checked yet.
/// </summary>
public sealed record PreviewStrategyQuery(string? Instrument, string? Timeframe, JsonElement? Strategy, double? TakerFee = null, double? MakerFee = null)
    : IRequest<StrategyPreviewOutcomeInternal>;
