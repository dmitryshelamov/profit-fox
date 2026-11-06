using MediatR;

using ProfitFox.Application.Backtests;
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

public sealed class GetOwnIdeaQueryHandler(
    IMarketSnapshotRepository snapshots,
    IScannerPairRepository pairs,
    ICandleRepository candles,
    IdeasSettings settings,
    BacktestSettings backtests) : IRequestHandler<GetOwnIdeaQuery, GetOwnIdeaResult>
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
        else if (type == IdeaType.StopEntry)
        {
            errors["type"] = IdeaQueries.NoOwnStopEntry;
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

        var instrument = new Instrument(category, request.Symbol);

        if (await snapshots.GetAsync(instrument, cancellationToken) is not { Metrics.Price: not null } row)
        {
            return new GetOwnIdeaResult(null, true, errors);
        }

        IdeaLevels levels = await IdeaLevels.ReadAsync(candles, row, interval, settings, cancellationToken);

        // Like the live ideas, a pair whose price step the list of instruments has not told yet has none.
        if (levels.Atr is not { } atr || await pairs.GetAsync(instrument, cancellationToken) is not { TickSize: { } tick })
        {
            return new GetOwnIdeaResult(null, false, errors);
        }

        var context = new IdeaContext(category, levels.Price, levels.WindowLow, levels.WindowHigh, atr, levels.Zones, levels.Spread, settings.StopBuffer, tick);

        IdeaPlan? plan = IdeaRules.Plan(type, side, context);

        return new GetOwnIdeaResult(plan, false, errors, tick, plan is null ? null : IdeaMoney.PerUsdt(plan, backtests.Policy().DefaultFees[category]));
    }
}