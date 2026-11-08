using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The reads of an account go to the exchange of its key (decision 2).</summary>
internal sealed class RoutedExchangeLedger(
    [FromKeyedServices(Exchange.Bybit)] IExchangeLedger bybit,
    [FromKeyedServices(Exchange.TInvest)] IExchangeLedger tinvest) : IExchangeLedger
{
    public Task<decimal> GetFreeAsync(ExchangeKey key, string coin, CancellationToken cancellationToken)
    {
        return Of(key).GetFreeAsync(key, coin, cancellationToken);
    }

    public Task<bool> IsHedgeModeAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Of(key).IsHedgeModeAsync(key, instrument, cancellationToken);
    }

    public Task<decimal> GetHoldingAsync(ExchangeKey key, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        return Of(key).GetHoldingAsync(key, instrument, baseCoin, cancellationToken);
    }

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Of(key).GetOpenOrdersAsync(key, instrument, cancellationToken);
    }

    public Task<ExchangeOrder?> FindOrderAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        return Of(key).FindOrderAsync(key, instrument, clientId, cancellationToken);
    }

    public Task<ExchangeOrder?> FindOrderByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        return Of(key).FindOrderByExchangeIdAsync(key, instrument, exchangeOrderId, cancellationToken);
    }

    public Task<IReadOnlyList<ExchangeOrder>> GetOrdersOfParentAsync(ExchangeKey key, Instrument instrument, string parentClientId, CancellationToken cancellationToken)
    {
        return Of(key).GetOrdersOfParentAsync(key, instrument, parentClientId, cancellationToken);
    }

    public Task<IReadOnlyList<ExchangeFill>> GetOrderFillsAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        return Of(key).GetOrderFillsAsync(key, instrument, exchangeOrderId, cancellationToken);
    }

    public Task<IReadOnlyList<ExchangeFill>> GetFillsAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        return Of(key).GetFillsAsync(key, instrument, clientId, cancellationToken);
    }

    public Task<FeeSchedule> GetFeesAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Of(key).GetFeesAsync(key, instrument, cancellationToken);
    }

    private IExchangeLedger Of(ExchangeKey key)
    {
        return key.Exchange == Exchange.TInvest ? tinvest : bybit;
    }
}