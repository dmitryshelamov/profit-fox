using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBots;

public sealed class GetBotsQueryHandler(IStrategyLibraryRepository library, IBacktestRepository backtests, IExchangeAccountRepository accounts)
    : IRequestHandler<GetBotsQuery, IReadOnlyList<BotListItem>>
{
    /// <summary>Three queries for the whole list: bots, their newest backtests and the accounts, without secrets.</summary>
    public async Task<IReadOnlyList<BotListItem>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> bots = await library.GetBotsAsync(cancellationToken);
        IReadOnlyDictionary<Guid, Backtest> last = await backtests.GetLastOfBotsAsync(cancellationToken);
        var byId = (await accounts.GetAllAsync(cancellationToken)).ToDictionary(account => account.Id);

        return [.. bots.Select(bot => new BotListItem(bot, last.GetValueOrDefault(bot.Id), bot.ExchangeAccountId is { } id ? byId.GetValueOrDefault(id) : null))];
    }
}