using ProfitFox.Domain.Strategies;
using ProfitFox.Domain.Positions;
using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;

namespace ProfitFox.Domain.Runs;

/// <summary>
///     The events of one pair in a run (spec hypothesis-bench): the orders of the strategy of the hypothesis in the period
///     go through the rule of one position (spec strategy-position), block by block on the minute path of each block. The
///     pair starts the period with the position closed. An order that comes while the position is open is skipped: it is
///     counted in <see cref="Skipped" />, gets no random entries and no row. Every other event gets its random entries from
///     the one generator of the pair, each a cycle opened outside the graph on the side of the event with the exit values
///     of its own candle. Without <c>withEventOutcomes</c> the cycle of an event is walked only to its end, which the next
///     event needs: its result is never computed.
/// </summary>
public sealed class PairEvaluation
{
    private readonly string _instrument;
    private readonly PairSteps _steps;
    private readonly RunDesign _design;
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

    public PairEvaluation(string instrument, PairSteps steps, RunDesign design, bool withEventOutcomes)
    {
        _instrument = instrument;
        _steps = steps;
        _design = design;
        _withEventOutcomes = withEventOutcomes;
        StrategyStep[] orders = [.. steps.Orders.Where(order => design.InPeriod(order.Moment))];
        _orders = orders.ToLookup(order => Runs.Blocks.Of(order.Moment, design.Hypothesis.Block));
        _randoms = new RandomEntries(instrument, orders.Select(order => order.Moment), design);
        Blocks = [.. _orders.Select(block => block.Key).Order()];
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
        TradeCosts costs = _design.Hypothesis.Costs;
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
            TradeFields[] valid = [.. trades.Where(trade => trade.Outcome.Status == TradeStatus.Ok).Select(trade => TradeFields.Of(trade.Outcome, _design.Hypothesis.Measure))];
            double? pseudo = valid.Length >= 2 ? valid[0].Y - valid.Skip(1).Average(trade => trade.Y) : null;

            rows.Add(new EventRow(
                _instrument,
                order.Moment,
                block,
                cycle.Status,
                cycle.Outcome,
                cycle.Outcome is { Status: TradeStatus.Ok } ? TradeFields.Of(cycle.Outcome, _design.Hypothesis.Measure) : null,
                trades,
                valid.Length,
                TradeFields.Mean(valid),
                pseudo));
        }

        return rows;
    }
}
