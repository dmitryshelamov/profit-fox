using MediatR;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ideas.Queries.GetOwnIdea;

public sealed class GetOwnIdeaQueryHandler(IMarketSnapshotRepository snapshots, ICandleRepository candles, IdeasSettings settings) : IRequestHandler<GetOwnIdeaQuery, GetOwnIdeaResult>
{
    public async Task<GetOwnIdeaResult> Handle(GetOwnIdeaQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }

        if (!IdeaCodes.TryParseType(request.Type ?? "", out IdeaType type))
        {
            errors["type"] = IdeaQueries.InvalidType;
        }

        if (!IdeaQueries.TryParseInterval(request.Interval, out Interval interval))
        {
            errors["interval"] = IdeaQueries.InvalidInterval;
        }

        if (!IdeaCodes.TryParseSide(request.Side ?? "", out IdeaSide side))
        {
            errors["side"] = IdeaQueries.InvalidSide;
        }
        else if (side == IdeaSide.Short && category == MarketCategory.Spot)
        {
            errors["side"] = IdeaQueries.NoShortOnSpot;
        }

        if (errors.Count > 0)
        {
            return new GetOwnIdeaResult(null, false, errors);
        }

        if (await snapshots.GetAsync(new Instrument(category, request.Symbol), cancellationToken) is not { Metrics.Price: not null } row)
        {
            return new GetOwnIdeaResult(null, true, errors);
        }

        IdeaLevels levels = await IdeaLevels.ReadAsync(candles, row, interval, settings, cancellationToken);

        if (levels.Atr is not { } atr)
        {
            return new GetOwnIdeaResult(null, false, errors);
        }

        var context = new IdeaContext(category, levels.Price, levels.WindowLow, levels.WindowHigh, atr, levels.Zones, levels.Spread, settings.StopBuffer);

        return new GetOwnIdeaResult(IdeaRules.Plan(type, side, context), false, errors);
    }
}