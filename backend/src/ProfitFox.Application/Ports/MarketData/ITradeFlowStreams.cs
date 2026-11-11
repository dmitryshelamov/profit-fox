using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>Every public trade of an instrument as the exchange sends it, for the current bar of the trade flow. A stream reconnects on its own; disposing the subscription ends it.</summary>
public interface ITradeFlowStreams
{
    /// <param name="instrument">The instrument whose trades are followed.</param>
    /// <param name="traded">Each trade, in the order the exchange sent them.</param>
    /// <param name="lost">The connection broke; trades are missing until <paramref name="restored" />.</param>
    /// <param name="restored">The connection is back after a gap.</param>
    /// <param name="closed">The subscription ended for good and no trade will come; a new subscription is needed.</param>
    /// <param name="cancellationToken">Bounds the wait for the subscription, not the subscription.</param>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused the subscription or did not answer.</exception>
    Task<IAsyncDisposable> SubscribeTradesAsync(
        Instrument instrument,
        Action<TradeEvent> traded,
        Action lost,
        Action restored,
        Action closed,
        CancellationToken cancellationToken);
}