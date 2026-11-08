using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.Market;

/// <summary>The public streams of the exchange the scanner keeps the whole market with, apart from the streams of the bots.</summary>
public interface IMarketStreams
{
    /// <summary>
    ///     Subscribes the closed minute candles of every instrument in batches spread over a few connections; a candle that is still forming
    ///     never comes. <paramref name="restored" /> tells that a connection is back after a break, when candles may have been missed.
    ///     Disposing the result takes every subscription off and closes the connections.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">The exchange refused a subscription.</exception>
    Task<IAsyncDisposable> SubscribeMinuteCandlesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, Candle> closed,
        Action restored,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Subscribes the liquidations of every linear instrument in batches spread over connections of their own, apart from the candles.
    ///     <paramref name="lost" /> and <paramref name="restored" /> tell, with the time they were seen, that a connection broke and that it is
    ///     back; the events between them are missed for good. Disposing the result takes every subscription off and closes the connections.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">The exchange refused a subscription.</exception>
    Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, LiquidationEvent> liquidated,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored,
        CancellationToken cancellationToken);
}