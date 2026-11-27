using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     Every set of a backtest on one pair in one pass over its history (change add-strategy-backtest, design.md, decision
///     4): the candles of each timeframe and the funding are read once by the caller, and each set runs its graph on the
///     candles of its timeframe. A category of ideas reads the minutes of each week in which at least one set has orders,
///     once for all of them, in the window of the week plus the longest time of its steps, and each set goes through its
///     orders and random entries on that path (<see cref="PairEvaluation" />). A strategy for a bot reads the minutes of
///     the whole history once, and each set walks them with its position (<see cref="PairCycles" />). Sets do not see each
///     other: a set gives the same numbers alone and among others.
/// </summary>
public static class PairBacktest
{
    /// <summary>
    ///     The result of each set on the pair, in the order of <paramref name="sets" />.
    /// </summary>
    /// <param name="symbol">The symbol of the pair.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="sets">The sets: their strategies with values and their designs, one history for all.</param>
    /// <param name="candles">The candles of the pair by timeframe, from the start of the data to the end of history.</param>
    /// <param name="funding">The funding of the pair over the same time, with the payment at the end.</param>
    /// <param name="minutes">Reads the minutes of the pair.</param>
    /// <param name="cancellationToken">Stops the calculation between weeks and candles.</param>
    /// <param name="named">
    ///     The candles of the pairs the graph names by timeframe and symbol (change add-ideas-and-market); null when it names
    ///     none.
    /// </param>
    /// <param name="openInterest">
    ///     The points of open interest of the pair over the same time (change add-idea-score); null when no graph of the sets
    ///     has the node «OI».
    /// </param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<IReadOnlyList<PairSetResult>> RunAsync(
        string symbol,
        StrategyKind kind,
        IReadOnlyList<BacktestPlan> sets,
        IReadOnlyDictionary<TimeSpan, IReadOnlyList<HistoryCandle>> candles,
        HistoryRead<HistoryPayment> funding,
        MinuteReader minutes,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named = null,
        IReadOnlyList<OpenInterestPoint>? openInterest = null)
    {
        if (sets.Count == 0)
        {
            return [];
        }

        return kind == StrategyKind.Idea
            ? await Ideas(symbol, sets, candles, funding, minutes, named, openInterest, cancellationToken)
            : await Bot(sets, candles, funding, minutes, named, openInterest, cancellationToken);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? Named(
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named,
        TimeSpan timeframe)
    {
        return named is not null && named.TryGetValue(timeframe, out IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? pairs) ? pairs : null;
    }

    private static async Task<IReadOnlyList<PairSetResult>> Ideas(
        string symbol,
        IReadOnlyList<BacktestPlan> sets,
        IReadOnlyDictionary<TimeSpan, IReadOnlyList<HistoryCandle>> candles,
        HistoryRead<HistoryPayment> funding,
        MinuteReader minutes,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named,
        IReadOnlyList<OpenInterestPoint>? openInterest,
        CancellationToken cancellationToken)
    {
        PairEvaluation[] evaluations =
        [
            .. sets.Select(set => new PairEvaluation(
                symbol,
                PairSteps.Of(set.Strategy, set.Design.Timeframe, symbol, candles[set.Design.Timeframe], funding.Items, Named(named, set.Design.Timeframe), openInterest),
                set.Design))
        ];
        List<EventRow>[] rows = [.. sets.Select(_ => new List<EventRow>())];
        foreach (long week in evaluations.SelectMany(evaluation => evaluation.Blocks).Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] busy = [.. Enumerable.Range(0, sets.Count).Where(index => evaluations[index].Blocks.Contains(week))];
            (DateTimeOffset From, DateTimeOffset To)[] spans = [.. busy.Select(index => evaluations[index].Span(week))];
            DateTimeOffset from = spans.Min(span => span.From);
            DateTimeOffset to = spans.Max(span => span.To);
            MinutePath path = MinutePaths.Build(from, to, await minutes(from, to, cancellationToken), funding);
            foreach (int index in busy)
            {
                rows[index].AddRange(evaluations[index].Evaluate(week, path));
            }
        }

        return [.. sets.Select((set, index) => new PairSetResult(set.Set, PairNumbers.Of(rows[index], evaluations[index].Skipped, evaluations[index].PastEnd), rows[index], Cycles: null))];
    }

    // One history for every set: the start and the end of the first are those of all.
    private static async Task<IReadOnlyList<PairSetResult>> Bot(
        IReadOnlyList<BacktestPlan> sets,
        IReadOnlyDictionary<TimeSpan, IReadOnlyList<HistoryCandle>> candles,
        HistoryRead<HistoryPayment> funding,
        MinuteReader minutes,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named,
        IReadOnlyList<OpenInterestPoint>? openInterest,
        CancellationToken cancellationToken)
    {
        BacktestDesign design = sets[0].Design;
        MinutePath path = MinutePaths.Build(design.Start, design.End, await minutes(design.Start, design.End, cancellationToken), funding);
        List<PairSetResult> results = [];
        foreach (BacktestPlan set in sets)
        {
            TradeCosts costs = set.Design.Costs;
            PositionResult result = PairCycles.Of(set.Strategy, set.Design.Timeframe, candles[set.Design.Timeframe], funding.Items, path, design.Start, design.End, costs, cancellationToken, Named(named, set.Design.Timeframe), openInterest);
            results.Add(new PairSetResult(set.Set, PairNumbers.Of(result), Rows: [], result));
        }

        return results;
    }
}
