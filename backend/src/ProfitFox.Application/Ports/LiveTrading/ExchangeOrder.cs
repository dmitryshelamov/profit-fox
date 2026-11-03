namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An order as the exchange knows it; <see cref="ClientId" /> is the id the bot gave it. The filled quantity, its value and
///     fee are totals of the order so far, the truth of its fills; <see cref="FeeCoin" /> is the coin its fee is taken in.
/// </summary>
public sealed record ExchangeOrder(
    string ClientId,
    string ExchangeId,
    ExchangeOrderState State,
    decimal FilledQuantity,
    string? RejectReason,
    decimal FilledValue = 0m,
    decimal Fee = 0m,
    string FeeCoin = "");