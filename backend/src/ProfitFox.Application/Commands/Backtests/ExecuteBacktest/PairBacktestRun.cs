using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     The sets of a backtest on one pair with its history read from the market-data service (spec market-history;
///     change add-strategy-backtest, design.md, decision 4): the candles of each timeframe of the sets from the start of the
///     data and the funding with the payment at the end are read once, the minutes as <see cref="PairBacktest" /> asks for
///     them, nothing after the end of history.
/// </summary>
public static class PairBacktestRun
{
    /// <param name="history">The reader of the backtest, bounded by the end of its history.</param>
    /// <param name="pair">The pair.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="plans">The sets; they share the end of history.</param>
    /// <param name="cancellationToken">Stops the reads and the calculation.</param>
    /// <param name="named">The candles of the pairs the graph names, read once for the backtest (<see cref="NamedAsync" />).</param>
    public static async Task<IReadOnlyList<PairSetResult>> RunAsync(
        MarketHistory history,
        Instrument pair,
        StrategyKind kind,
        IReadOnlyList<BacktestPlan> plans,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named = null)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        DateTimeOffset end = plans[0].Design.End;
        Dictionary<TimeSpan, IReadOnlyList<HistoryCandle>> candles = [];
        foreach (TimeSpan timeframe in plans.Select(plan => plan.Design.Timeframe).Distinct())
        {
            candles[timeframe] = (await history.Candles(pair.Id, timeframe, HistoryPeriod.DataStart, end, cancellationToken)).Items;
        }

        HistoryRead<HistoryPayment> funding = await history.FundingThrough(pair.Id, HistoryPeriod.DataStart, end, cancellationToken);
        return await PairBacktest.RunAsync(
            pair.Symbol,
            kind,
            plans,
            candles,
            funding,
            (from, to, token) => history.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, token),
            cancellationToken,
            named);
    }

    /// <summary>
    ///     The candles of the pairs the graph names, of each timeframe of the sets, read once for the whole backtest and added
    ///     to its hash (change add-ideas-and-market, spec market-history «Свечи названной пары»); null when the graph names
    ///     none. A named pair that is no longer listed has no candles.
    /// </summary>
    public static async Task<IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>?> NamedAsync(
        MarketHistory history,
        IReadOnlyList<string> named,
        IReadOnlyList<Instrument> listed,
        IReadOnlyList<BacktestPlan> plans,
        CancellationToken cancellationToken)
    {
        if (named.Count == 0 || plans.Count == 0)
        {
            return null;
        }

        DateTimeOffset end = plans[0].Design.End;
        Instrument[] pairs = [.. listed.Where(pair => named.Contains(pair.Symbol, StringComparer.Ordinal)).OrderBy(pair => pair.Symbol, StringComparer.Ordinal)];
        var candles = new Dictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>();
        foreach (TimeSpan timeframe in plans.Select(plan => plan.Design.Timeframe).Distinct())
        {
            var byPair = new Dictionary<string, IReadOnlyList<HistoryCandle>>(StringComparer.Ordinal);
            foreach (Instrument pair in pairs)
            {
                byPair[pair.Symbol] = (await history.Candles(pair.Id, timeframe, HistoryPeriod.DataStart, end, cancellationToken)).Items;
            }

            candles[timeframe] = byPair;
        }

        return candles;
    }
}
