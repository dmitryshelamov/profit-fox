using MediatR;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ideas.Queries.GetZones;

public sealed class GetZonesQueryHandler(IMarketSnapshotRepository snapshots, ICandleRepository candles, IdeasSettings settings) : IRequestHandler<GetZonesQuery, GetZonesResult>
{
    public async Task<GetZonesResult> Handle(GetZonesQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }

        if (!IdeaQueries.TryParseInterval(request.Interval, out Interval interval))
        {
            errors["interval"] = IdeaQueries.InvalidInterval;
        }

        if (errors.Count > 0)
        {
            return new GetZonesResult(null, null, false, errors);
        }

        if (await snapshots.GetAsync(new Instrument(category, request.Symbol), cancellationToken) is not { Metrics.Price: not null } row)
        {
            return new GetZonesResult(null, null, true, errors);
        }

        IdeaLevels levels = await IdeaLevels.ReadAsync(candles, row, interval, settings, cancellationToken);

        return new GetZonesResult(levels.Price, levels.Zones, false, errors);
    }
}