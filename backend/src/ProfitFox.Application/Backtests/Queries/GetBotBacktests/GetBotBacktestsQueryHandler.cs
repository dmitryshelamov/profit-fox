using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetBotBacktests;

public sealed class GetBotBacktestsQueryHandler(IStrategyLibraryRepository library, IBacktestRepository backtests)
    : IRequestHandler<GetBotBacktestsQuery, IReadOnlyList<Backtest>?>
{
    public async Task<IReadOnlyList<Backtest>?> Handle(GetBotBacktestsQuery request, CancellationToken cancellationToken)
    {
        return await library.GetBotAsync(request.BotId, cancellationToken) is null ? null : await backtests.GetByBotAsync(request.BotId, cancellationToken);
    }
}