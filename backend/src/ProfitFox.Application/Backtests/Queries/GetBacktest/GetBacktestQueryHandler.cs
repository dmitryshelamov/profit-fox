using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetBacktest;

public sealed class GetBacktestQueryHandler(IBacktestRepository backtests, IStrategyLibraryRepository library) : IRequestHandler<GetBacktestQuery, GetBacktestResult?>
{
    /// <summary>A backtest goes with its bot, so the bot is there whenever the backtest is.</summary>
    public async Task<GetBacktestResult?> Handle(GetBacktestQuery request, CancellationToken cancellationToken)
    {
        Backtest? backtest = await backtests.GetAsync(request.Id, cancellationToken);

        if (backtest is null)
        {
            return null;
        }

        Bot bot = (await library.GetBotAsync(backtest.BotId, cancellationToken))!;
        int latest = (await library.GetLatestVersionNumberAsync(bot.StrategyId, cancellationToken))!.Value;

        return new GetBacktestResult(backtest, backtest.BotChanged(bot, latest));
    }
}