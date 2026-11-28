namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The rules of a USDT perpetual at the exchange (<c>/v5/market/instruments-info</c>; design.md, decision 7): the step
///     and the least of the quantity, the least value of an order, the step of the price, the greatest leverage, and whether
///     it trades now.
/// </summary>
public sealed record PairRules(string Symbol, decimal QuantityStep, decimal MinQuantity, decimal MinNotional, decimal TickSize, decimal MaxLeverage, bool Trading);
