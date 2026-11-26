using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     The cells of control of a set (spec strategy-backtest «Случайные входы», «Разница со случайным входом»; change
///     fix-backtest-random-control, design.md, decision 5): the random entries of the orders of a set with one moment and one
///     side are the same for each of them, so their result is kept once for the pair «moment, side» — the sums of the fields
///     of the valid entries and their number. The entries come in the order of the symbols of their pairs, the order of the
///     second pass, so the sums are the same whatever the number of Workers. An order counts when its cell has at least two
///     valid entries; its difference is its result minus the mean of its cell.
/// </summary>
public sealed class ControlCells
{
    /// <summary>
    ///     The fewest valid random entries an order counts with.
    /// </summary>
    public const int Fewest = 2;

    private readonly Dictionary<PairOrder, Cell> _cells = [];

    /// <summary>
    ///     The cells with at least one valid entry: the moment and the side, the mean of the entries and their number, in the
    ///     order of time and then of side.
    /// </summary>
    public IReadOnlyList<(PairOrder Order, TradeFields Mean, int Valid)> All =>
        [.. _cells.OrderBy(cell => cell.Key.Moment).ThenBy(cell => cell.Key.Side).Select(cell => (cell.Key, cell.Value.Mean, cell.Value.Count))];

    /// <summary>
    ///     Adds the random entries of a pair; an entry whose trade is left out is not valid and adds nothing.
    /// </summary>
    public void Add(IEnumerable<RandomTrade> trades)
    {
        foreach (RandomTrade trade in trades)
        {
            if (trade.Outcome.Status != TradeStatus.Ok)
            {
                continue;
            }

            var order = new PairOrder(trade.Entry, trade.Side);
            var fields = TradeFields.Of(trade.Outcome);
            _cells[order] = _cells.TryGetValue(order, out Cell cell) ? cell.With(fields) : new Cell(fields, 1);
        }
    }

    /// <summary>
    ///     The number of valid random entries of the orders with this moment and side.
    /// </summary>
    public int Valid(PairOrder order)
    {
        return _cells.TryGetValue(order, out Cell cell) ? cell.Count : 0;
    }

    /// <summary>
    ///     The mean of the valid random entries of the orders with this moment and side; none without one.
    /// </summary>
    public TradeFields? Mean(PairOrder order)
    {
        return _cells.TryGetValue(order, out Cell cell) ? cell.Mean : null;
    }

    /// <summary>
    ///     The differences of the orders that count, in the order of <paramref name="orders" />, with their weeks.
    /// </summary>
    public IReadOnlyList<WeekDifference> Differences(IEnumerable<OrderTrade> orders)
    {
        List<WeekDifference> differences = [];
        foreach (OrderTrade order in orders)
        {
            if (_cells.TryGetValue(order.Order, out Cell cell) && cell.Count >= Fewest)
            {
                differences.Add(new WeekDifference(order.Week, order.Y - cell.Mean.Y));
            }
        }

        return differences;
    }

    // The sums of the fields of the valid entries of a cell and their number.
    private readonly record struct Cell(TradeFields Sum, int Count)
    {
        public TradeFields Mean => new TradeFields(Sum.Y / Count, Sum.Gross / Count, Sum.Fees / Count, Sum.Slippage / Count, Sum.Funding / Count, Sum.Net / Count);

        public Cell With(TradeFields fields)
        {
            return new Cell(
                new TradeFields(Sum.Y + fields.Y, Sum.Gross + fields.Gross, Sum.Fees + fields.Fees, Sum.Slippage + fields.Slippage, Sum.Funding + fields.Funding, Sum.Net + fields.Net),
                Count + 1);
        }
    }
}