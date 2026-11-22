using System.Globalization;

using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Presentation.Contracts.Errors;

namespace ProfitFox.Presentation.Contracts.Backtests.Converters;

/// <summary>
///     The backtests, their sets and pairs and the errors of a request as the API sends them.
/// </summary>
public static class BacktestResponses
{
    public static BacktestResponse Of(BacktestInternal backtest)
    {
        return new BacktestResponse(
            backtest.Id,
            new BacktestStrategyResponse(backtest.StrategyId, backtest.StrategyName, backtest.StrategyVersion, StrategyKinds.Name(backtest.Kind)),
            Of(backtest.Settings),
            backtest.Status,
            backtest.QueuedAt,
            backtest.StartedAt,
            backtest.FinishedAt,
            backtest.Worker,
            backtest.PairsDone,
            backtest.PairsTotal,
            backtest.SetCount,
            [.. backtest.Sets.Select(Of)],
            backtest.Warning,
            backtest.EngineVersion,
            backtest.DataHash,
            backtest.ReadSeconds,
            backtest.ComputeSeconds,
            backtest.Reason,
            backtest.BestAmount,
            backtest.Source is { } source ? new BacktestSourceResponse(source.Backtest, source.Set) : null);
    }

    public static BacktestSetResponse Of(BacktestSetOutcome set)
    {
        return new BacktestSetResponse(
            set.Set.Number,
            Timeframes.Name(set.Set.Timeframe),
            [.. set.Set.Values.Select(value => new ParameterValueResponse(value.Key, value.Value))],
            set.Numbers is { } numbers ? Of(numbers) : null);
    }

    public static BacktestPairResponse Of(PairSummary pair)
    {
        return new BacktestPairResponse(pair.Pair, Of(pair.Numbers));
    }

    public static SetNumbersResponse Of(SetSummary numbers)
    {
        return new SetNumbersResponse(
            numbers.Orders,
            numbers.Trades,
            numbers.PairsWithTrades,
            numbers.Mean,
            numbers.Amount,
            numbers.WinShare,
            new ExclusionsResponse(numbers.Excluded.Plan, numbers.Excluded.Coverage, numbers.Excluded.Funding, numbers.Excluded.NoTrade, numbers.Excluded.Period),
            numbers.Skipped,
            numbers.OpenCycles,
            numbers.OpenAmount,
            numbers.Difference is { } difference ? new RandomDifferenceResponse(difference.Mean, difference.Weeks, difference.T) : null);
    }

    public static RequestError Of(BacktestError error)
    {
        return new RequestError(error.Code, error.Reason)
        {
            Field = error.Field,
            Parameter = error.Parameter,
            Pair = error.Pair,
            Node = error.Graph?.Node,
            Pin = error.Graph?.Pin,
            Setting = error.Graph?.Setting,
            Wire = error.Graph?.Wire is { } wire ? new RequestWire(wire.From.ToString(), wire.To.ToString()) : null
        };
    }

    private static BacktestSettingsResponse Of(BacktestSettings settings)
    {
        return new BacktestSettingsResponse(
            [.. settings.Values.Select(parameter => new ParameterValuesResponse(parameter.Name, parameter.Values))],
            [.. settings.Timeframes.Select(Timeframes.Name)],
            Date(settings.Start),
            Date(settings.End),
            settings.Pairs,
            settings.TakerFee,
            settings.MakerFee);
    }

    private static string Date(DateTimeOffset moment)
    {
        return moment.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
