using MediatR;

using ProfitFox.Application.Contracts.MarketData;
using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Application.Queries.MarketData;

internal sealed class GetInstrumentsQueryHandler(IInstrumentCatalog catalog) : IRequestHandler<GetInstrumentsQuery, IReadOnlyList<InstrumentInternal>>
{
    /// <exception cref="HistoryUnavailableException">The catalog did not answer.</exception>
    public async Task<IReadOnlyList<InstrumentInternal>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        return [.. (await catalog.LinearUsdtPerpetuals(cancellationToken)).Select(instrument => instrument.ToInternal())];
    }
}
