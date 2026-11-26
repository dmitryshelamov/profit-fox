using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     The sets of a backtest over its pairs with their history read from the market-data service (spec market-history;
///     change fix-backtest-random-control, design.md, decisions 4 and 6): the candles of each timeframe of the sets from the
///     start of the data and the funding with the payment at the end, the minutes as <see cref="PairBacktest" /> asks for
///     them, nothing after the end of history. A category of ideas reads the candles and the funding of a pair in each of
///     its two passes; only the first read goes into the hash of the backtest, the minutes of the second pass go in too.
/// </summary>
public static class PairBacktestRun
{
    /// <param name="history">The reader of the backtest, bounded by the end of its history.</param>
    /// <param name="pairs">The pairs in the order of their symbols.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="plans">The sets; they share the end of history.</param>
    /// <param name="cancellationToken">Stops the reads and the calculation.</param>
    /// <param name="named">The candles of the pairs the graph names, read once for the backtest (<see cref="NamedAsync" />).</param>
    /// <param name="passed">Told the halves of a pair each pass of a pair counts for (<see cref="BacktestPasses" />).</param>
    /// <param name="computed">Told the results of the sets on each pair as the last pass computes them.</param>
    public static async Task<IReadOnlyList<SetTotals>> RunAsync(
        MarketHistory history,
        IReadOnlyList<Instrument> pairs,
        StrategyKind kind,
        IReadOnlyList<BacktestPlan> plans,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named = null,
        Action<int>? passed = null,
        Action<string, IReadOnlyList<PairSetResult>>? computed = null)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        return await BacktestPasses.RunAsync(pairs, kind, plans, Reader(history, plans), cancellationToken, named, passed, computed);
    }

    // The history of a pair for a pass: a second read of the candles and the funding is not added to the hash.
    private static PairHistoryReader Reader(MarketHistory history, IReadOnlyList<BacktestPlan> plans)
    {
        DateTimeOffset end = plans[0].Design.End;
        TimeSpan[] timeframes = [.. plans.Select(plan => plan.Design.Timeframe).Distinct()];
        MarketHistory again = history.Again();
        return async (pair, second, cancellationToken) =>
        {
            MarketHistory reads = second ? again : history;
            Dictionary<TimeSpan, IReadOnlyList<HistoryCandle>> candles = [];
            foreach (TimeSpan timeframe in timeframes)
            {
                candles[timeframe] = (await reads.Candles(pair.Id, timeframe, HistoryPeriod.DataStart, end, cancellationToken)).Items;
            }

            HistoryRead<HistoryPayment> funding = await reads.FundingThrough(pair.Id, HistoryPeriod.DataStart, end, cancellationToken);
            return new PairHistory(candles, funding, (from, to, token) => history.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, token));
        };
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