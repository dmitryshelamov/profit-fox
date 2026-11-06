using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>
///     What the exchange says an account holds: in <c>linear</c> the position of the symbol, a short below zero, on the spot the
///     coin of the instrument; the orders of bots it has open and those it has finished, and the same for the orders of idea trades.
/// </summary>
public sealed record ExchangeHoldings(
    decimal Position,
    IReadOnlyCollection<ClientOrderId> OpenOrders,
    IReadOnlyCollection<ClientOrderId> FinishedOrders,
    IReadOnlyCollection<IdeaTradeOrderId>? OpenTradeOrders = null,
    IReadOnlyCollection<IdeaTradeOrderId>? FinishedTradeOrders = null);