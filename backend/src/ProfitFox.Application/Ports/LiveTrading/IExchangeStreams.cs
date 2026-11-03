using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     Live events of the exchange. A stream reconnects on its own; <c>restored</c> tells that it is back after a gap, whose events
///     are lost and must be read again. Disposing the subscription ends it.
/// </summary>
public interface IExchangeStreams
{
    /// <summary>Closed candles of the instrument; a candle that is still forming never comes.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused the subscription or did not answer.</exception>
    Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken);

    /// <summary>Order updates and executions of the account, of every instrument.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused the key or the subscription, or did not answer.</exception>
    Task<IAsyncDisposable> SubscribeAccountAsync(
        ExchangeKey key,
        Action<ExchangeOrder> order,
        Action<ExchangeFill> fill,
        Action restored,
        CancellationToken cancellationToken);
}