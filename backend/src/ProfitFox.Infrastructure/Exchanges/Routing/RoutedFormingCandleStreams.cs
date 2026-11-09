using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The forming candle comes from the exchange of the category (decision 11).</summary>
internal sealed class RoutedFormingCandleStreams(
    [FromKeyedServices(Exchange.Bybit)] IFormingCandleStreams bybit,
    [FromKeyedServices(Exchange.TInvest)] IFormingCandleStreams tinvest) : IFormingCandleStreams
{
    public Task<IAsyncDisposable> SubscribeAsync(
        Instrument instrument,
        Interval interval,
        Action<FormingCandle> update,
        Action lost,
        Action restored,
        Action closed,
        CancellationToken cancellationToken)
    {
        return (instrument.Category.Exchange() == Exchange.TInvest ? tinvest : bybit).SubscribeAsync(instrument, interval, update, lost, restored, closed, cancellationToken);
    }
}