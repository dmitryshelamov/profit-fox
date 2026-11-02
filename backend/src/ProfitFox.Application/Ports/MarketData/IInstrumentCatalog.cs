using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>The instruments the exchange lists in a market category.</summary>
public interface IInstrumentCatalog
{
    /// <exception cref="ExchangeUnavailableException">The exchange cannot answer for now.</exception>
    /// <exception cref="ExchangeException">The exchange rejected the request.</exception>
    Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken);
}