using MediatR;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaGuards;

public sealed class GetIdeaGuardsQueryHandler(IIdeaTradingSettingsRepository settings, IIdeaTradeRepository trades, TimeProvider time) : IRequestHandler<GetIdeaGuardsQuery, IdeaGuardsState>
{
    public async Task<IdeaGuardsState> Handle(GetIdeaGuardsQuery request, CancellationToken cancellationToken)
    {
        IdeaTradingSettings saved = await settings.GetAsync(cancellationToken);
        IReadOnlyList<IdeaTrade> all = await trades.GetAllAsync(cancellationToken);
        IdeaTrade[] open = [.. all.Where(trade => trade.IsOpen)];

        return new IdeaGuardsState(
            open.Length,
            saved.MaxOpenTrades,
            open.Sum(trade => trade.Risk),
            saved.MaxTotalRisk,
            DailyLossReader.Of(all, saved.DailyLossLimit, time.GetUtcNow()),
            open.Count(trade => trade.Side == IdeaSide.Long),
            open.Count(trade => trade.Side == IdeaSide.Short),
            saved.MaxSameDirection,
            AutoBudget.AutosUsed(open),
            saved.AutosBudget);
    }
}