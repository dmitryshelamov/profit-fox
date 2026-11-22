using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     The orders of one pair of a strategy by plan in a backtest (spec strategy-backtest «Заявка при открытой позиции в
///     прогоне», «Случайные входы»): the orders in history go through the rule of one position (spec strategy-position),
///     block by block on the minute path of each block. The pair starts the history with the position closed. An order that
///     comes while the position is open is skipped: it is counted in <see cref="Skipped" />, gets no random entries and no
///     row. Every other order gets its random entries from the one generator of the pair, each a cycle opened outside the
///     graph on the side of the order with the exit values of its own candle. An order whose time ends after the end of
///     history is counted in <see cref="PastEnd" /> and is not simulated. Without <c>withEventOutcomes</c> the cycle of an
///     order is walked only to its end, which the next order needs: its result is never computed.
/// </summary>
public sealed class PairEvaluation
{
    private readonly string _instrument;
    private readonly PairSteps _steps;
    private readonly BacktestDesign _design;
    private readonly bool _withEventOutcomes;
    private readonly ILookup<long, StrategyStep> _orders;
    private readonly RandomEntries _randoms;
    private readonly PairPosition _position = new PairPosition();
    private long? _lastBlock;

    /// <summary>
    ///     The blocks that have events of the pair in the period, in time order.
    /// </summary>
    public IReadOnlyList<long> Blocks { get; }

    /// <summary>
    ///     The events skipped so far because the position was open.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    ///     The orders from the start of history whose time ends after its end (spec strategy-backtest «Сделки набора»,
    ///     «Срок за концом истории»): they give no trade.
    /// </summary>
    public int PastEnd { get; }

    public PairEvaluation(string instrument, PairSteps steps, BacktestDesign design, bool withEventOutcomes = true)
    {
        _instrument = instrument;
        _steps = steps;
        _design = design;
        _withEventOutcomes = withEventOutcomes;
        StrategyStep[] orders = [.. steps.Orders.Where(order => design.Fits(order.Moment, order.After))];
        PastEnd = steps.Orders.Count(order => order.Moment >= design.Start && !design.Fits(order.Moment, order.After));
        _orders = orders.ToLookup(order => Runs.Blocks.Of(order.Moment, design.Block));
        _randoms = new RandomEntries(instrument, orders.Select(order => order.Moment), design, moment => steps.At(moment).After);
        Blocks = [.. _orders.Select(block => block.Key).Order()];
    }

    /// <summary>
    ///     The orders of a pair in a run of a hypothesis: its period is the history, and the horizon of its plan is the time
    ///     of every step.
    /// </summary>
    public PairEvaluation(string instrument, PairSteps steps, RunDesign design, bool withEventOutcomes)
        : this(instrument, steps, design.ToBacktest(), withEventOutcomes)
    {
    }

    /// <summary>
    ///     The minutes the trades of a block can touch (change add-strategy-backtest, design.md, decision 4): the block plus
    ///     the longest time of its orders and of the candles its random entries may fall on, within history.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Span(long block)
    {
        DateTimeOffset start = Runs.Blocks.Start(block, _design.Block);
        TimeSpan orders = _orders[block].Max(order => order.After ?? TimeSpan.Zero);
        TimeSpan steps = _steps.Longest(start, start + _design.BlockLength);
        return _design.Span(block, orders > steps ? orders : steps);
    }

    /// <summary>
    ///     The rows of the events of a block on its minute path. Blocks go in the order of time: the position and the
    ///     generator of random entries pass from one block to the next.
    /// </summary>
    public List<EventRow> Evaluate(long block, MinutePath path)
    {
        if (block <= _lastBlock)
        {
            throw new InvalidOperationException($"Blocks go in the order of time: {block} is not after {_lastBlock}.");
        }

        _lastBlock = block;
        TradeCosts costs = _design.Costs;
        IReadOnlyList<DateTimeOffset> candidates = _randoms.Candidates(block);
        List<EventRow> rows = [];
        foreach (StrategyStep order in _orders[block].OrderBy(order => order.Moment))
        {
            if (_position.Enter(path, order, costs, _withEventOutcomes) is not { } cycle)
            {
                Skipped++;
                continue;
            }

            EntrySide side = order.Entry!.Value;
            RandomTrade[] trades = [.. _randoms.Draw(candidates).Select(entry => new RandomTrade(entry, Cycle.Open(path, _steps.At(entry), side, costs).Outcome!))];
            TradeFields[] valid = [.. trades.Where(trade => trade.Outcome.Status == TradeStatus.Ok).Select(trade => TradeFields.Of(trade.Outcome, _design.Measure))];
            double? pseudo = valid.Length >= 2 ? valid[0].Y - valid.Skip(1).Average(trade => trade.Y) : null;

            rows.Add(new EventRow(
                _instrument,
                order.Moment,
                block,
                cycle.Status,
                cycle.Outcome,
                cycle.Outcome is { Status: TradeStatus.Ok } ? TradeFields.Of(cycle.Outcome, _design.Measure) : null,
                trades,
                valid.Length,
                TradeFields.Mean(valid),
                pseudo) { NetAmount = cycle.Result?.NetAmount });
        }

        return rows;
    }
}
