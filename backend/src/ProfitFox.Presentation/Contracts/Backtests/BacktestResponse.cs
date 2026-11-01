using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Backtests;

namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>Money, rates and the speed are decimal strings: JSON numbers lose precision in JavaScript clients.</summary>
public sealed record BacktestResponse(
    Guid Id,
    Guid BotId,
    Guid StrategyId,
    int StrategyVersion,
    string Category,
    string Symbol,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    JsonObject Parameters,
    string SearchMode,
    SimulationResponse Simulation,
    RulesResponse Rules,
    string Status,
    string? Error,
    ProgressResponse Progress,
    string? Speed,
    DateTimeOffset? ActualStart,
    IReadOnlyList<GapResponse> Gaps,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool BotChanged)
{
    public static BacktestResponse Of(Backtest backtest, bool botChanged)
    {
        return new BacktestResponse(
            backtest.Id,
            backtest.BotId,
            backtest.StrategyId,
            backtest.StrategyVersion,
            backtest.Instrument.Category.ToCode(),
            backtest.Instrument.Symbol,
            backtest.Interval.ToCode(),
            backtest.From,
            backtest.To,
            BacktestJson.WriteParameters(backtest.Parameters),
            backtest.SearchMode.ToCode(),
            new SimulationResponse(
                Text(backtest.Simulation.Balance),
                Text(backtest.Simulation.LimitFeePercent),
                Text(backtest.Simulation.MarketFeePercent),
                Text(backtest.Simulation.SlippagePercent),
                backtest.Simulation.LimitFillMode.ToCode()),
            new RulesResponse(Text(backtest.Rules.TickSize), Text(backtest.Rules.QuantityStep), Text(backtest.Rules.MinQuantity), Text(backtest.Rules.MinAmount)),
            backtest.Status.ToCode(),
            backtest.Error,
            ProgressResponse.Of(backtest),
            backtest.Speed is { } speed ? Text(speed) : null,
            backtest.ActualStart,
            [.. backtest.Gaps.Select(gap => new GapResponse(gap.From, gap.To))],
            backtest.CreatedAt,
            backtest.StartedAt,
            backtest.FinishedAt,
            botChanged);
    }

    internal static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}