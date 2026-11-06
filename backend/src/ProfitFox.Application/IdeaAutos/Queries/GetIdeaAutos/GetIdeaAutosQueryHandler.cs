using MediatR;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutos;

public sealed class GetIdeaAutosQueryHandler(
    IIdeaAutoRepository autos,
    IExchangeAccountRepository accounts,
    IPaperIdeaTradeRepository paper,
    IIdeaTradeRepository trades,
    IIdeaAutoRefusalRepository refusals,
    TimeProvider time) : IRequestHandler<GetIdeaAutosQuery, IReadOnlyList<IdeaAutoSummary>>
{
    public async Task<IReadOnlyList<IdeaAutoSummary>> Handle(GetIdeaAutosQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<IdeaAuto> list = await autos.GetAllAsync(cancellationToken);
        var names = (await accounts.GetAllAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        ILookup<Guid, IdeaTrade> papers = (await paper.GetAllAsync(cancellationToken)).Select(trade => trade.Trade).Where(IsClosed).ToLookup(trade => trade.AutoId!.Value);
        ILookup<Guid, IdeaTrade> real = (await trades.GetAllAsync(cancellationToken)).Where(trade => trade.AutoId is not null && IsClosed(trade)).ToLookup(trade => trade.AutoId!.Value);
        IReadOnlyDictionary<Guid, int> today = await refusals.CountSinceAsync(new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero), cancellationToken);

        return
        [
            .. list.Select(auto =>
            {
                decimal[] resultsInR = [.. papers[auto.Id].Select(trade => trade.ResultR!.Value)];

                return new IdeaAutoSummary(
                    auto,
                    names[auto.AccountId],
                    resultsInR.Length,
                    resultsInR.Length > 0 ? resultsInR.Average() : null,
                    resultsInR.Sum(),
                    real[auto.Id].Count(),
                    real[auto.Id].Sum(trade => trade.Result!.Value),
                    today.GetValueOrDefault(auto.Id));
            })
        ];
    }

    // Closed by the target, by the stop or by hand: the trades that have a result.
    private static bool IsClosed(IdeaTrade trade)
    {
        return trade.Result is not null;
    }
}