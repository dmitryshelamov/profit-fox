using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The closed candles come from the exchange of the category, the events of an account from the exchange of its key (decision 2).</summary>
internal sealed class RoutedExchangeStreams(
    [FromKeyedServices(Exchange.Bybit)] IExchangeStreams bybit,
    [FromKeyedServices(Exchange.TInvest)] IExchangeStreams tinvest) : IExchangeStreams
{
    public Task<IAsyncDisposable> SubscribeCandlesAsync(Instrument instrument, Interval interval, Action<Candle> closed, Action restored, CancellationToken cancellationToken)
    {
        return (instrument.Category.Exchange() == Exchange.TInvest ? tinvest : bybit).SubscribeCandlesAsync(instrument, interval, closed, restored, cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeAccountAsync(
        ExchangeKey key,
        Action<ExchangeOrder> order,
        Action<ExchangeFill> fill,
        Action restored,
        CancellationToken cancellationToken)
    {
        return (key.Exchange == Exchange.TInvest ? tinvest : bybit).SubscribeAccountAsync(key, order, fill, restored, cancellationToken);
    }
}