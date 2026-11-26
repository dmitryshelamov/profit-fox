using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     A backtest over its pairs, pair by pair in the order given (change fix-backtest-random-control, design.md, decision 4).
///     A category of ideas goes in two passes. The first finds the orders of every set on every pair and the close of the first
///     candle of each timeframe; between the passes each set gets its orders over every pair and the control groups of its
///     timeframe. The second computes the trades and the random entries of every pair and adds the random entries to the cells
///     of control of each set in the order of the pairs; at the end the difference of each order that counts is made from its
///     cell. Between the passes only the orders and the first closes stay in memory, and to the end the numbers of the pairs
///     and what is kept of each order with a trade. A strategy for a bot goes in one pass.
/// </summary>
public static class BacktestPasses
{
    /// <param name="pairs">The pairs, in the order they are computed in: the order of their symbols.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="sets">The sets; they share the history.</param>
    /// <param name="read">Reads the history of a pair for a pass.</param>
    /// <param name="cancellationToken">Stops the backtest between pairs, weeks and candles.</param>
    /// <param name="named">The candles of the pairs the graph names by timeframe and symbol; null when it names none.</param>
    /// <param name="passed">
    ///     Told after each pair of each pass the halves of a pair it counts for: one for each pass of a category of ideas, two
    ///     for the one pass of a bot.
    /// </param>
    /// <param name="computed">Told the results of the sets on each pair as the last pass computes them.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<IReadOnlyList<SetTotals>> RunAsync(
        IReadOnlyList<Instrument> pairs,
        StrategyKind kind,
        IReadOnlyList<BacktestPlan> sets,
        PairHistoryReader read,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named = null,
        Action<int>? passed = null,
        Action<string, IReadOnlyList<PairSetResult>>? computed = null)
    {
        return kind == StrategyKind.Idea
            ? await Ideas(pairs, sets, read, named, passed, computed, cancellationToken)
            : await Bot(pairs, sets, read, named, passed, computed, cancellationToken);
    }

    private static async Task<IReadOnlyList<SetTotals>> Ideas(
        IReadOnlyList<Instrument> pairs,
        IReadOnlyList<BacktestPlan> sets,
        PairHistoryReader read,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named,
        Action<int>? passed,
        Action<string, IReadOnlyList<PairSetResult>>? computed,
        CancellationToken cancellationToken)
    {
        // The first pass: the orders of every set on every pair and the first close of each timeframe.
        Dictionary<string, IReadOnlyList<PairOrder>>[] orders = [.. sets.Select(_ => new Dictionary<string, IReadOnlyList<PairOrder>>(StringComparer.Ordinal))];
        Dictionary<TimeSpan, Dictionary<string, DateTimeOffset>> firsts = [];
        foreach (Instrument pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairHistory history = await read(pair, again: false, cancellationToken);
            IReadOnlyList<PairFirstPass> found = PairBacktest.Orders(pair.Symbol, sets, history.Candles, history.Funding, cancellationToken, named);
            for (int index = 0; index < sets.Count; index++)
            {
                orders[index][pair.Symbol] = found[index].Orders;
                if (found[index].FirstClose is { } first)
                {
                    Dictionary<string, DateTimeOffset> closes = firsts.TryGetValue(sets[index].Design.Timeframe, out Dictionary<string, DateTimeOffset>? known) ? known : firsts[sets[index].Design.Timeframe] = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                    closes[pair.Symbol] = first;
                }
            }

            passed?.Invoke(1);
        }

        // Between the passes: one control group of a week for every set of a timeframe.
        Dictionary<(TimeSpan, TimeBlock, int), ControlGroups> groups = [];
        SetControl[] controls =
        [
            .. sets.Select((set, index) =>
            {
                BacktestDesign design = set.Design;
                (TimeSpan, TimeBlock, int) key = (design.Timeframe, design.Block, design.Randoms);
                if (!groups.TryGetValue(key, out ControlGroups? group))
                {
                    group = new ControlGroups(firsts.TryGetValue(design.Timeframe, out Dictionary<string, DateTimeOffset>? closes) ? closes : [], design.Block, design.Randoms);
                    groups[key] = group;
                }

                return new SetControl(new SetOrders(orders[index], design.Block), group);
            })
        ];

        // The second pass: the trades and the random entries, the random entries into the cells of each set.
        ControlCells[] cells = [.. sets.Select(_ => new ControlCells())];
        List<(string Pair, SetNumbers Numbers, OrderTrade[] Trades)>[] byPair = [.. sets.Select(_ => new List<(string, SetNumbers, OrderTrade[])>())];
        foreach (Instrument pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairHistory history = await read(pair, again: true, cancellationToken);
            IReadOnlyList<PairSetResult> results = await PairBacktest.RunAsync(pair.Symbol, StrategyKind.Idea, sets, history.Candles, history.Funding, history.Minutes, cancellationToken, named, controls);
            computed?.Invoke(pair.Symbol, results);
            for (int index = 0; index < results.Count; index++)
            {
                cells[index].Add(results[index].Randoms);
                byPair[index].Add((pair.Symbol, results[index].Numbers, [.. results[index].Rows.Select(OrderTrade.Of).OfType<OrderTrade>()]));
            }

            passed?.Invoke(1);
        }

        // The end: the differences of each pair from the cells of its set.
        return
        [
            .. sets.Select((_, index) =>
            {
                (string, SetNumbers)[] numbers = [.. byPair[index].Select(pair => (pair.Pair, pair.Numbers with { Differences = cells[index].Differences(pair.Trades) }))];
                return new SetTotals(numbers.Aggregate(SetNumbers.Zero, (total, pair) => total + pair.Item2), numbers, cells[index]);
            })
        ];
    }

    private static async Task<IReadOnlyList<SetTotals>> Bot(
        IReadOnlyList<Instrument> pairs,
        IReadOnlyList<BacktestPlan> sets,
        PairHistoryReader read,
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named,
        Action<int>? passed,
        Action<string, IReadOnlyList<PairSetResult>>? computed,
        CancellationToken cancellationToken)
    {
        SetNumbers[] totals = [.. sets.Select(_ => SetNumbers.Zero)];
        List<(string Pair, SetNumbers Numbers)>[] byPair = [.. sets.Select(_ => new List<(string, SetNumbers)>())];
        foreach (Instrument pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairHistory history = await read(pair, again: false, cancellationToken);
            IReadOnlyList<PairSetResult> results = await PairBacktest.RunAsync(pair.Symbol, StrategyKind.Bot, sets, history.Candles, history.Funding, history.Minutes, cancellationToken, named);
            computed?.Invoke(pair.Symbol, results);
            for (int index = 0; index < results.Count; index++)
            {
                totals[index] += results[index].Numbers;
                byPair[index].Add((pair.Symbol, results[index].Numbers));
            }

            passed?.Invoke(2);
        }

        return [.. sets.Select((_, index) => new SetTotals(totals[index], byPair[index], Cells: null))];
    }
}