using System.Text.Json;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;

internal static class BacktestDbConverter
{
    internal static BacktestDb FromDomain(Backtest backtest)
    {
        return new BacktestDb
        {
            Id = backtest.Id,
            BotId = backtest.BotId,
            StrategyId = backtest.StrategyId,
            StrategyVersion = backtest.StrategyVersion,
            Category = backtest.Instrument.Category.ToCode(),
            Symbol = backtest.Instrument.Symbol,
            Interval = backtest.Interval.ToCode(),
            From = backtest.From,
            To = backtest.To,
            Parameters = BacktestJson.WriteParameters(backtest.Parameters).ToJsonString(),
            Balance = backtest.Simulation.Balance,
            LimitFeePercent = backtest.Simulation.LimitFeePercent,
            MarketFeePercent = backtest.Simulation.MarketFeePercent,
            SlippagePercent = backtest.Simulation.SlippagePercent,
            LimitFillMode = backtest.Simulation.LimitFillMode.ToCode(),
            TickSize = backtest.Rules.TickSize,
            QuantityStep = backtest.Rules.QuantityStep,
            MinQuantity = backtest.Rules.MinQuantity,
            MinAmount = backtest.Rules.MinAmount,
            SearchMode = backtest.SearchMode.ToCode(),
            Status = backtest.Status.ToCode(),
            Error = backtest.Error,
            ImportJobId = backtest.ImportJobId,
            ActualStart = backtest.ActualStart,
            Gaps = JsonSerializer.Serialize(backtest.Gaps.Select(gap => new CandleGapDb(gap.From, gap.To))),
            LoadedCandles = backtest.LoadedCandles,
            ExpectedCandles = backtest.ExpectedCandles,
            CalculatedCombinations = backtest.CalculatedCombinations,
            TotalCombinations = backtest.TotalCombinations,
            Speed = backtest.Speed,
            StalledRestarts = backtest.StalledRestarts,
            CreatedAt = backtest.CreatedAt,
            StartedAt = backtest.StartedAt,
            FinishedAt = backtest.FinishedAt
        };
    }

    internal static Backtest ToDomain(BacktestDb backtest)
    {
        using var parameters = JsonDocument.Parse(backtest.Parameters);
        MarketDataCodes.TryParseCategory(backtest.Category, out MarketCategory category);
        MarketDataCodes.TryParseInterval(backtest.Interval, out Interval interval);
        BacktestCodes.TryParseMode(backtest.LimitFillMode, out Domain.Entities.Backtests.Enums.LimitFillMode mode);
        BacktestCodes.TryParseSearchMode(backtest.SearchMode, out Domain.Entities.Backtests.Enums.SearchMode searchMode);

        return new Backtest(
            backtest.Id,
            backtest.BotId,
            backtest.StrategyId,
            backtest.StrategyVersion,
            new Instrument(category, backtest.Symbol),
            interval,
            backtest.From,
            backtest.To,
            BacktestJson.ReadParameters(parameters.RootElement),
            new SimulationSettings(backtest.Balance, backtest.LimitFeePercent, backtest.MarketFeePercent, backtest.SlippagePercent, mode),
            new InstrumentRules(backtest.TickSize, backtest.QuantityStep, backtest.MinQuantity, backtest.MinAmount),
            searchMode,
            BacktestCodes.ParseStatus(backtest.Status),
            backtest.Error,
            backtest.ImportJobId,
            backtest.ActualStart,
            [.. JsonSerializer.Deserialize<CandleGapDb[]>(backtest.Gaps)!.Select(gap => new CandleGap(gap.From, gap.To))],
            backtest.LoadedCandles,
            backtest.ExpectedCandles,
            backtest.CalculatedCombinations,
            backtest.TotalCombinations,
            backtest.Speed,
            backtest.StalledRestarts,
            backtest.CreatedAt,
            backtest.StartedAt,
            backtest.FinishedAt);
    }
}