using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     A set of a backtest of a category of ideas on one pair (spec strategy-backtest «Заявка при открытой позиции в
///     прогоне», «Случайные входы»; change fix-backtest-random-control, design.md, decisions 1 and 4): the orders of the pair
///     go through the rule of one position (spec strategy-position), week by week on the minute path of each week. The pair
///     starts the history with the position closed. An order that comes while the position is open is skipped: it is counted
///     in <see cref="Skipped" /> and has no row. An order whose time ends after the end of history is counted in
///     <see cref="PastEnd" /> and is not simulated. In a week where the pair is in the control group, it takes a random entry
///     for each moment and side of the orders of the other pairs of the week, unless it has an order of its own at that
///     moment: a cycle opened outside the graph at that moment, on that side, with the exit values of this pair. The position
///     of the pair does not hold a random entry, and a random entry does not hold the position. An entry whose trade does not
///     fit in history is not taken.
/// </summary>
public sealed class PairEvaluation
{
    private readonly string _instrument;
    private readonly PairSteps _steps;
    private readonly BacktestDesign _design;
    private readonly ILookup<long, StrategyStep> _orders;
    private readonly Dictionary<long, PairOrder[]> _randoms;
    private readonly PairPosition _position = new PairPosition();
    private long? _lastBlock;

    /// <summary>
    ///     The weeks that have orders of the pair or random entries on it, in time order.
    /// </summary>
    public IReadOnlyList<long> Blocks { get; }

    /// <summary>
    ///     The orders skipped so far because the position was open.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    ///     The orders from the start of history whose time ends after its end (spec strategy-backtest «Сделки набора»,
    ///     «Срок за концом истории»): they give no trade.
    /// </summary>
    public int PastEnd { get; }

    /// <param name="instrument">The symbol of the pair.</param>
    /// <param name="steps">The steps of the set on the pair.</param>
    /// <param name="design">What the set is computed on.</param>
    /// <param name="control">The orders of the set over every pair and the control groups; none gives no random entries.</param>
    public PairEvaluation(string instrument, PairSteps steps, BacktestDesign design, SetControl? control = null)
    {
        _instrument = instrument;
        _steps = steps;
        _design = design;
        StrategyStep[] orders = [.. steps.Orders.Where(order => design.Fits(order.Moment, order.After))];
        PastEnd = steps.Orders.Count(order => order.Moment >= design.Start && !design.Fits(order.Moment, order.After));
        _orders = orders.ToLookup(order => Runs.Blocks.Of(order.Moment, design.Block));
        _randoms = control is null ? [] : Randoms(instrument, steps, design, control);
        Blocks = [.. _orders.Select(block => block.Key).Union(_randoms.Keys).Order()];
    }

    /// <summary>
    ///     The minutes the trades of a week can touch (change add-strategy-backtest, design.md, decision 4): the week plus the
    ///     longest time of its orders and of its steps, which the random entries take their time from, within history.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Span(long block)
    {
        DateTimeOffset start = Runs.Blocks.Start(block, _design.Block);
        TimeSpan orders = _orders[block].Select(order => order.After ?? TimeSpan.Zero).DefaultIfEmpty(TimeSpan.Zero).Max();
        TimeSpan steps = _steps.Longest(start, start + _design.BlockLength);
        return _design.Span(block, orders > steps ? orders : steps);
    }

    /// <summary>
    ///     The rows of the orders of a week and the random entries of the pair in it, on its minute path. Weeks go in the order
    ///     of time: the position passes from one week to the next.
    /// </summary>
    public PairWeek Evaluate(long block, MinutePath path)
    {
        if (block <= _lastBlock)
        {
            throw new InvalidOperationException($"Blocks go in the order of time: {block} is not after {_lastBlock}.");
        }

        _lastBlock = block;
        TradeCosts costs = _design.Costs;
        List<EventRow> rows = [];
        foreach (StrategyStep order in _orders[block].OrderBy(order => order.Moment))
        {
            if (_position.Enter(path, order, costs) is not { } cycle)
            {
                Skipped++;
                continue;
            }

            rows.Add(new EventRow(
                _instrument,
                order.Moment,
                order.Entry!.Value,
                block,
                cycle.Status,
                cycle.Outcome,
                cycle.Outcome is { Status: TradeStatus.Ok } ? TradeFields.Of(cycle.Outcome) : null)
            { NetAmount = cycle.Result?.NetAmount });
        }

        RandomTrade[] randoms = _randoms.TryGetValue(block, out PairOrder[]? entries)
            ? [.. entries.Select(entry => new RandomTrade(_instrument, entry.Moment, entry.Side, Cycle.Open(path, _steps.At(entry.Moment), entry.Side, costs).Outcome!))]
            : [];
        return new PairWeek(rows, randoms);
    }

    // The random entries of the pair by week: the moments and sides of the orders of the other pairs in the weeks where the
    // pair is in the control group, but where the pair has an order of its own and those whose trade does not fit.
    private static Dictionary<long, PairOrder[]> Randoms(string instrument, PairSteps steps, BacktestDesign design, SetControl control)
    {
        Dictionary<long, PairOrder[]> weeks = [];
        foreach (long week in control.Orders.Weeks)
        {
            if (!control.Groups.Contains(week, instrument))
            {
                continue;
            }

            PairOrder[] entries =
            [
                .. control.Orders.Others(week, instrument)
                    .Where(order => !control.Orders.Has(instrument, order.Moment) && design.Fits(order.Moment, steps.At(order.Moment).After))
            ];
            if (entries.Length > 0)
            {
                weeks[week] = entries;
            }
        }

        return weeks;
    }
}