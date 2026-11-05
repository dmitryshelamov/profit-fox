using MediatR;

using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaTrades;

public sealed class GetIdeaTradesQueryHandler(IIdeaTradeRepository trades, IExchangeAccountRepository accounts) : IRequestHandler<GetIdeaTradesQuery, IReadOnlyList<IdeaTradeView>>
{
    public async Task<IReadOnlyList<IdeaTradeView>> Handle(GetIdeaTradesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<IdeaTrade> all = await trades.GetAllAsync(cancellationToken);
        var names = (await accounts.GetAllAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);

        return [.. all.Select(trade => new IdeaTradeView(trade, names.GetValueOrDefault(trade.AccountId, string.Empty)))];
    }
}