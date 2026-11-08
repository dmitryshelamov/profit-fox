using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The catalog of the exchange of the category (decision 8); <see cref="CachedInstrumentCatalog" /> sits over it.</summary>
internal sealed class RoutedInstrumentCatalog(
    [FromKeyedServices(Exchange.Bybit)] IInstrumentCatalog bybit,
    [FromKeyedServices(Exchange.TInvest)] IInstrumentCatalog tinvest) : IInstrumentCatalog
{
    public Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        return (category.Exchange() == Exchange.TInvest ? tinvest : bybit).ListAsync(category, cancellationToken);
    }
}