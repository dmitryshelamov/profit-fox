using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The history of the exchange of the category: Bybit for <c>spot</c> and <c>linear</c>, T-Invest for <c>moex</c> (decision 8).</summary>
internal sealed class RoutedMarketHistoryClient(
    [FromKeyedServices(Exchange.Bybit)] IMarketHistoryClient bybit,
    [FromKeyedServices(Exchange.TInvest)] IMarketHistoryClient tinvest) : IMarketHistoryClient
{
    public IAsyncEnumerable<IReadOnlyList<Candle>> GetCandlePagesAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Of(instrument.Category).GetCandlePagesAsync(instrument, interval, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<Candle>> GetCandlesOnceAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Of(instrument.Category).GetCandlesOnceAsync(instrument, interval, from, to, cancellationToken);
    }

    public Task<decimal> GetLastPriceAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        return Of(instrument.Category).GetLastPriceAsync(instrument, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, decimal>> GetLastPricesAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        return Of(category).GetLastPricesAsync(category, cancellationToken);
    }

    public Task<bool> InstrumentExistsAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        return Of(instrument.Category).InstrumentExistsAsync(instrument, cancellationToken);
    }

    private IMarketHistoryClient Of(MarketCategory category)
    {
        return category.Exchange() == Exchange.TInvest ? tinvest : bybit;
    }
}