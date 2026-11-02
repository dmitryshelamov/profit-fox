using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The metrics of a combination, as <see cref="MetricsObserver" /> collects them, with its positions and equity curve. An order cancelled
///     without a fill is dropped at once, so the replay keeps only the active orders and those with fills.
/// </summary>
public sealed class DetailsObserver(decimal balance) : IReplayObserver
{
    public const int MaxEquityPoints = 5000;

    private readonly MetricsObserver _metrics = new MetricsObserver(balance);
    private readonly SortedDictionary<int, CollectedPosition> _positions = [];
    private readonly Dictionary<string, (CollectedPosition Position, CollectedOrder Order)> _active = [];
    private readonly List<(DateTimeOffset At, Candle Candle, decimal Equity)> _closes = [];
    private RunState? _last;

    public void Placed(PlaceOrder order, DateTimeOffset at, int position)
    {
        CollectedPosition collected = PositionOf(position);
        var placed = new CollectedOrder(order);
        collected.Orders.Add(placed);
        _active[order.OrderId] = (collected, placed);
    }

    public void Cancelled(string orderId, DateTimeOffset at)
    {
        (CollectedPosition position, CollectedOrder order) = _active[orderId];
        _active.Remove(orderId);
        position.Orders.Remove(order);
    }

    /// <summary>A fill of the replay fills its order whole.</summary>
    public void Filled(ReplayFill fill, RunState state)
    {
        _metrics.Filled(fill, state);
        (CollectedPosition position, CollectedOrder order) = _active[fill.OrderId];
        _active.Remove(fill.OrderId);
        order.Status = BacktestOrderStatus.Filled;
        order.Fills.Add(new BacktestFill(fill.At, fill.Price, fill.Quantity, fill.Fee, fill.FeeAsset));
        position.Add(fill, state.Direction);

        if (state.Closed is { } closed)
        {
            PositionOf(closed.Number).Closed = closed;
            PositionOf(state.Position).CarriedQuantity = state.Quantity;
        }

        _last = state;
    }

    public void BarClosed(DateTimeOffset at, Candle candle, RunState state)
    {
        _metrics.BarClosed(at, candle, state);
        _closes.Add((at, candle, state.Equity(candle.Close.Value)));
        _last = state;
    }

    /// <summary>The positions with a fill or an active order, oldest first.</summary>
    public BacktestDetails Details()
    {
        return new BacktestDetails(
            _metrics.Metrics(),
            [.. _positions.Values.Where(position => position.HasFills || position.Orders.Count > 0).Select(position => position.Value(_last))],
            Curve());
    }

    private CollectedPosition PositionOf(int number)
    {
        if (!_positions.TryGetValue(number, out CollectedPosition? position))
        {
            position = new CollectedPosition(number);
            _positions[number] = position;
        }

        return position;
    }

    /// <summary>Closes in equal groups, so that there are no more than <see cref="MaxEquityPoints" /> of them.</summary>
    private IReadOnlyList<EquityPoint> Curve()
    {
        int size = (_closes.Count + MaxEquityPoints - 1) / MaxEquityPoints;

        return
        [
            .. _closes.Chunk(Math.Max(size, 1)).Select(group => new EquityPoint(
                group[^1].At,
                group[^1].Equity,
                group.Min(close => close.Equity),
                group[0].Candle.OpenTime,
                group[0].Candle.Open.Value,
                group.Max(close => close.Candle.High.Value),
                group.Min(close => close.Candle.Low.Value),
                group[^1].Candle.Close.Value))
        ];
    }

    private sealed class CollectedOrder(PlaceOrder order)
    {
        public List<BacktestFill> Fills { get; } = [];

        public BacktestOrderStatus Status { get; set; } = BacktestOrderStatus.Active;

        public BacktestOrder Value()
        {
            return new BacktestOrder(order.OrderId, order.NodePath, order.Side, order.Type, order.Price, order.Quantity, order.Amount, Status, Fills);
        }
    }

    /// <summary>A position being collected: its orders, its entries and exits by quantity.</summary>
    private sealed class CollectedPosition(int number)
    {
        private decimal _entryValue;
        private decimal _entryQuantity;
        private decimal _exitValue;
        private decimal _exitQuantity;
        private DateTimeOffset? _openedAt;

        public List<CollectedOrder> Orders { get; } = [];

        public ClosedPosition? Closed { get; set; }

        public decimal CarriedQuantity { get; set; }

        public bool HasFills => _openedAt is not null;

        public void Add(ReplayFill fill, StrategyDirection direction)
        {
            _openedAt ??= fill.At;

            if ((fill.Side == OrderSide.Buy) == (direction == StrategyDirection.Long))
            {
                _entryValue += fill.Price * fill.Quantity;
                _entryQuantity += fill.Quantity;
            }
            else
            {
                _exitValue += fill.Price * fill.Quantity;
                _exitQuantity += fill.Quantity;
            }
        }

        /// <summary>The current position takes its state, PnL and fees from the last state of the run.</summary>
        public BacktestPosition Value(RunState? last)
        {
            RunState? current = last?.Position == number ? last : null;

            return new BacktestPosition(
                number,
                Closed is null ? current?.Status ?? PositionStatus.Waiting : PositionStatus.Closed,
                _openedAt,
                Closed?.At,
                CarriedQuantity,
                _entryQuantity == 0m ? null : _entryValue / _entryQuantity,
                _exitQuantity == 0m ? null : _exitValue / _exitQuantity,
                Closed?.RealizedPnl ?? current?.RealizedPnl ?? 0m,
                Closed?.Fees ?? current?.Fees ?? 0m,
                [.. Orders.Select(order => order.Value())]);
        }
    }
}