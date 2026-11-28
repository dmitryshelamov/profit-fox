using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     A market order of the bench (change take-idea-trades, design.md, decision 4): the pair, the side, the quantity and the
///     link id; an entry carries the stop and, when there is one, the target of the position, a close only makes the position
///     smaller.
/// </summary>
public sealed record ExchangeOrderRequest(string Symbol, TradeSide Side, decimal Quantity, string LinkId, decimal? Stop, decimal? Target, bool ReduceOnly);
