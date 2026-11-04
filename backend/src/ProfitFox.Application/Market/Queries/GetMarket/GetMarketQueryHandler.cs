using MediatR;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Market.Queries.GetMarket;

public sealed class GetMarketQueryHandler(IMarketSnapshotRepository snapshots, IScannerStateRepository state, TimeProvider time)
    : IRequestHandler<GetMarketQuery, GetMarketResult>
{
    public async Task<GetMarketResult> Handle(GetMarketQuery request, CancellationToken cancellationToken)
    {
        ScannerState scanner = await state.GetAsync(cancellationToken);
        IReadOnlyList<MarketSnapshotRow> rows = await snapshots.GetAllAsync(cancellationToken);

        return new GetMarketResult(scanner.RestPausedUntil > time.GetUtcNow() ? scanner : scanner with { RestPausedUntil = null }, rows);
    }
}