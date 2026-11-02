using MediatR;

using ProfitFox.Application.Backtests.Queries.GetBacktest;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetBotBacktests;

public sealed class GetBotBacktestsQueryHandler(IStrategyLibraryRepository library, IBacktestRepository backtests)
    : IRequestHandler<GetBotBacktestsQuery, IReadOnlyList<GetBacktestResult>?>
{
    /// <summary>A bot goes with its strategy, so the latest version is there whenever the bot is.</summary>
    public async Task<IReadOnlyList<GetBacktestResult>?> Handle(GetBotBacktestsQuery request, CancellationToken cancellationToken)
    {
        Bot? bot = await library.GetBotAsync(request.BotId, cancellationToken);

        if (bot is null)
        {
            return null;
        }

        int latest = (await library.GetLatestVersionNumberAsync(bot.StrategyId, cancellationToken))!.Value;

        return [.. (await backtests.GetByBotAsync(request.BotId, cancellationToken)).Select(backtest => new GetBacktestResult(backtest, backtest.BotChanged(bot, latest)))];
    }
}