using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Backtests.Queries.GetResultDetails;

public sealed class GetResultDetailsQueryHandler(IBacktestRepository backtests, IStrategyLibraryRepository library, ICandleRepository candles)
    : IRequestHandler<GetResultDetailsQuery, BacktestDetails?>
{
    /// <summary>
    ///     One replay at the strategy version, rules and settings of the backtest with the values of the result: the same code that gave
    ///     the result, so the metrics of the details are the metrics of the result.
    /// </summary>
    public async Task<BacktestDetails?> Handle(GetResultDetailsQuery request, CancellationToken cancellationToken)
    {
        Backtest? backtest = await backtests.GetAsync(request.BacktestId, cancellationToken);
        BacktestResult? result = backtest is null ? null : await backtests.GetResultAsync(backtest.Id, request.Combination, cancellationToken);

        if (backtest is null || result is null)
        {
            return null;
        }

        StrategyVersion version = (await library.GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, cancellationToken))!;
        IReadOnlyList<Candle> period = await candles.GetAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, int.MaxValue, cancellationToken);
        var details = new DetailsObserver(backtest.Simulation.Balance);
        backtest.Replay(version.Snapshot, result.ParameterValues).Run(backtest.From, period, details);

        return details.Details();
    }
}