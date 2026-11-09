using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An execution of an order as the exchange reports it; <see cref="FeeCoin" /> is the coin its fee was taken in and
///     <see cref="Instrument" /> the instrument it was made on, <c>null</c> when the system does not trade that market. An execution of the
///     stop or the target of an entry has no client id; <see cref="ExchangeOrderId" /> is the exchange's id of the order it belongs to. <see cref="Kind" /> tells a trade from a liquidation or an
///     auto-deleveraging of the exchange, and <see cref="Side" /> is the side of the execution, <c>null</c> where the exchange does not tell it.
/// </summary>
public sealed record ExchangeFill(string ExecId, string ClientId, decimal Quantity, decimal Price, decimal Fee, string FeeCoin, DateTimeOffset At, Instrument? Instrument = null, string? ExchangeOrderId = null, ExchangeFillKind Kind = ExchangeFillKind.Trade, OrderSide? Side = null);