using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBots;

public sealed class GetBotsQueryHandler(IStrategyLibraryRepository library, IBacktestRepository backtests) : IRequestHandler<GetBotsQuery, IReadOnlyList<BotListItem>>
{
    public async Task<IReadOnlyList<BotListItem>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> bots = await library.GetBotsAsync(cancellationToken);
        IReadOnlyDictionary<Guid, Backtest> last = await backtests.GetLastOfBotsAsync(cancellationToken);

        return [.. bots.Select(bot => new BotListItem(bot, last.GetValueOrDefault(bot.Id)))];
    }
}