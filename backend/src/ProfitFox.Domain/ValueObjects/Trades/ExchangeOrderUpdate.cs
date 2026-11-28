using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The state of an order at the exchange: the pair, the link id of the order, the id the exchange gave it, where it is,
///     what was filled, and why it was refused or cancelled.
/// </summary>
public sealed record ExchangeOrderUpdate(
    string Symbol,
    string? OrderLinkId,
    string OrderId,
    ExchangeOrderState State,
    decimal Filled,
    string? Reason) : ExchangeMessage(Symbol);
