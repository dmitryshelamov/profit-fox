namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An order as the exchange knows it; <see cref="ClientId" /> is the id the bot gave it. The filled quantity, its value and
///     fee are totals of the order so far, the truth of its fills; <see cref="FeeCoin" /> is the coin its fee is taken in. The stop or the
///     target the exchange makes for an entry has no client id: <see cref="ParentClientId" /> is the client id of that entry and
///     <see cref="StopKind" /> says which of the two it is.
/// </summary>
public sealed record ExchangeOrder(
    string ClientId,
    string ExchangeId,
    ExchangeOrderState State,
    decimal FilledQuantity,
    string? RejectReason,
    decimal FilledValue = 0m,
    decimal Fee = 0m,
    string FeeCoin = "",
    string? ParentClientId = null,
    StopOrderKind StopKind = StopOrderKind.None);