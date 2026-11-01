using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;

internal static class BacktestResultDbConverter
{
    internal static BacktestResultDb FromDomain(BacktestResult result)
    {
        BacktestMetrics metrics = result.Metrics;

        return new BacktestResultDb
        {
            BacktestId = result.BacktestId,
            Combination = result.Combination,
            ParameterValues = StrategyJson.WriteValues(result.ParameterValues).ToJsonString(),
            StartEquity = metrics.StartEquity,
            EndEquity = metrics.EndEquity,
            Pnl = metrics.Pnl,
            RealizedPnl = metrics.RealizedPnl,
            UnrealizedPnl = metrics.UnrealizedPnl,
            ReturnPercent = metrics.ReturnPercent,
            MaxDrawdownPercent = metrics.MaxDrawdownPercent,
            ReturnToDrawdown = metrics.ReturnToDrawdown,
            ClosedTrades = metrics.ClosedTrades,
            WinRatePercent = metrics.WinRatePercent,
            Fills = metrics.Fills,
            Fees = metrics.Fees,
            MaxPositionCost = metrics.MaxPositionCost,
            MaxPositionCostPercent = metrics.MaxPositionCostPercent,
            MaxCapitalUsed = metrics.MaxCapitalUsed,
            TimeInPositionPercent = metrics.TimeInPositionPercent,
            TimeUnderWaterPercent = metrics.TimeUnderWaterPercent,
            LongestUnderWaterCandles = metrics.LongestUnderWaterCandles,
            SkippedOrders = metrics.SkippedOrders,
            Candles = metrics.Candles,
            CalculationTime = result.CalculationTime
        };
    }

    internal static BacktestResult ToDomain(BacktestResultDb result)
    {
        using var values = JsonDocument.Parse(result.ParameterValues);

        return new BacktestResult(
            result.BacktestId,
            result.Combination,
            StrategyJson.ParseValues(values.RootElement),
            new BacktestMetrics(
                result.StartEquity,
                result.EndEquity,
                result.Pnl,
                result.RealizedPnl,
                result.UnrealizedPnl,
                result.ReturnPercent,
                result.MaxDrawdownPercent,
                result.ReturnToDrawdown,
                result.ClosedTrades,
                result.WinRatePercent,
                result.Fills,
                result.Fees,
                result.MaxPositionCost,
                result.MaxPositionCostPercent,
                result.MaxCapitalUsed,
                result.TimeInPositionPercent,
                result.TimeUnderWaterPercent,
                result.LongestUnderWaterCandles,
                result.SkippedOrders,
                result.Candles),
            result.CalculationTime);
    }
}