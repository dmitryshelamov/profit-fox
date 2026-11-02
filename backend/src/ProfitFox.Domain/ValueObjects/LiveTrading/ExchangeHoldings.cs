namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>
///     What the exchange says an account holds: in <c>linear</c> the position of the symbol, a short below zero, on the spot the
///     coin of the instrument; the orders of bots it has open and those it has finished.
/// </summary>
public sealed record ExchangeHoldings(
    decimal Position,
    IReadOnlyCollection<ClientOrderId> OpenOrders,
    IReadOnlyCollection<ClientOrderId> FinishedOrders);