using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarketPair;

public sealed class GetMarketPairQueryHandler(IMarketSnapshotRepository snapshots) : IRequestHandler<GetMarketPairQuery, GetMarketPairResult>
{
    public async Task<GetMarketPairResult> Handle(GetMarketPairQuery request, CancellationToken cancellationToken)
    {
        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            return new GetMarketPairResult(null, new Dictionary<string, string> { ["category"] = MarketQueries.InvalidCategory });
        }

        var instrument = new Instrument(category, request.Symbol);
        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups = await snapshots.GetSetupsAsync(cancellationToken);

        return new GetMarketPairResult(await snapshots.GetAsync(instrument, cancellationToken), new Dictionary<string, string>(), setups.GetValueOrDefault(instrument) ?? []);
    }
}