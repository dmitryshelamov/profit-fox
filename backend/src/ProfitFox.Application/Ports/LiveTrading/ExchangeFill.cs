using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An execution of an order as the exchange reports it; <see cref="FeeCoin" /> is the coin its fee was taken in and
///     <see cref="Instrument" /> the instrument it was made on, <c>null</c> when the system does not trade that market.
/// </summary>
public sealed record ExchangeFill(string ExecId, string ClientId, decimal Quantity, decimal Price, decimal Fee, string FeeCoin, DateTimeOffset At, Instrument? Instrument = null);