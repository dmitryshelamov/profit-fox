using MediatR;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     A pair is of the set when the catalog lists it now.
/// </summary>
internal sealed class GetMarketPairQueryHandler(IInstrumentCatalog catalog, IMarketPairRepository market) : IRequestHandler<GetMarketPairQuery, MarketPairInternal?>
{
    /// <exception cref="Exceptions.HistoryUnavailableException">The catalog did not answer.</exception>
    public async Task<MarketPairInternal?> Handle(GetMarketPairQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Instrument> listed = await catalog.LinearUsdtPerpetuals(cancellationToken);
        return listed.FirstOrDefault(pair => pair.Symbol == request.Symbol) is { } found
            ? new MarketPairInternal(found, await market.FindAsync(found.Symbol, cancellationToken))
            : null;
    }
}
