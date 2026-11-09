using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The orders, the cancels and the leverage of an account go to the exchange of its key (decision 2).</summary>
internal sealed class RoutedOrderGateway(
    [FromKeyedServices(Exchange.Bybit)] IOrderGateway bybit,
    [FromKeyedServices(Exchange.TInvest)] IOrderGateway tinvest) : IOrderGateway
{
    public Task<OrderOutcome> PlaceAsync(ExchangeKey key, Instrument instrument, OrderRequest order, CancellationToken cancellationToken)
    {
        return Of(key).PlaceAsync(key, instrument, order, cancellationToken);
    }

    public Task<OrderOutcome> CancelAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        return Of(key).CancelAsync(key, instrument, clientId, cancellationToken);
    }

    public Task<OrderOutcome> CancelByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        return Of(key).CancelByExchangeIdAsync(key, instrument, exchangeOrderId, cancellationToken);
    }

    public Task<OrderOutcome> SetLeverageAsync(ExchangeKey key, Instrument instrument, int leverage, CancellationToken cancellationToken)
    {
        return Of(key).SetLeverageAsync(key, instrument, leverage, cancellationToken);
    }

    private IOrderGateway Of(ExchangeKey key)
    {
        return key.Exchange == Exchange.TInvest ? tinvest : bybit;
    }
}