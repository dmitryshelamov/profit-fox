using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Every update of the candle of an instrument and interval as the exchange sends it, the forming one and its closing. A stream
///     reconnects on its own; disposing the subscription ends it.
/// </summary>
public interface IFormingCandleStreams
{
    /// <param name="instrument">The instrument whose candle is followed.</param>
    /// <param name="interval">The interval of the candle.</param>
    /// <param name="update">Each update, in the order the exchange sent them.</param>
    /// <param name="lost">The connection broke; updates are missing until <paramref name="restored" />.</param>
    /// <param name="restored">The connection is back after a gap.</param>
    /// <param name="closed">The subscription ended for good and no update will come; a new subscription is needed.</param>
    /// <param name="cancellationToken">Bounds the wait for the subscription, not the subscription.</param>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused the subscription or did not answer.</exception>
    Task<IAsyncDisposable> SubscribeAsync(
        Instrument instrument,
        Interval interval,
        Action<FormingCandle> update,
        Action lost,
        Action restored,
        Action closed,
        CancellationToken cancellationToken);
}