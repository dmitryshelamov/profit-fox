using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Application.Contracts.Backtests.Converters;

public static class BacktestInternalConverter
{
    public static BacktestInternal ToInternal(this Backtest backtest)
    {
        return new BacktestInternal(
            backtest.Id,
            backtest.StrategyId,
            backtest.StrategyName,
            backtest.StrategyVersion,
            backtest.Kind,
            backtest.Settings,
            backtest.Sets,
            backtest.SetCount,
            backtest.Source,
            backtest.Status,
            backtest.QueuedAt,
            backtest.StartedAt,
            backtest.FinishedAt,
            backtest.LeaseOwner,
            backtest.PairsDone,
            backtest.PairsTotal,
            backtest.EngineVersion,
            backtest.DataHash,
            backtest.ReadSeconds,
            backtest.ComputeSeconds,
            backtest.Reason,
            backtest.BestAmount,
            Backtest.SurvivorWarning);
    }

    public static LastBacktestInternal ToLastInternal(this Backtest backtest)
    {
        return new LastBacktestInternal(backtest.Id, backtest.Status, backtest.StrategyVersion);
    }
}
