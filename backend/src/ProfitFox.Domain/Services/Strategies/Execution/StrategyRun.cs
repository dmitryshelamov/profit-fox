using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>One execution of a valid strategy: takes events one by one and answers each with order commands.</summary>
public sealed class StrategyRun
{
    private readonly IReadOnlyList<RunNode> _nodes;
    private readonly Dictionary<string, int> _indexByPath;
    private readonly IReadOnlyDictionary<string, NodeValue> _parameters;
    private readonly Dictionary<string, NodeValue?>[] _outputs;
    private readonly object[] _states;
    private readonly Dictionary<string, ActiveOrder> _orders = [];
    private Position _position;
    private ClosedPosition? _closed;
    private readonly OrderSide _entrySide;
    private readonly StrategyDirection _direction;
    private readonly FeeSchedule _fees;
    private readonly InstrumentRules _rules;
    private readonly List<OrderCommand> _commands = [];
    private decimal _cash;
    private decimal _pastPnl;
    private int _lastOrderNumber;
    private StrategyEvent _event = null!;
    private Bar? _lastBar;

    private StrategyRun(IReadOnlyList<RunNode> nodes, IReadOnlyDictionary<string, NodeValue> parameters, StrategyDirection direction, TradingConditions conditions)
    {
        _entrySide = direction == StrategyDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        _cash = conditions.Balance;
        _position = new Position(1, direction, conditions.Rules.MinQuantity);
        _direction = direction;
        _fees = conditions.Fees;
        _rules = conditions.Rules;
        _nodes = nodes;
        _parameters = parameters;
        _indexByPath = nodes.Select((node, index) => (node.Path, index)).ToDictionary(entry => entry.Path, entry => entry.index);
        _outputs = [.. nodes.Select(_ => new Dictionary<string, NodeValue?>())];
        _states = [.. nodes.Select(StateOf)];
    }

    private static object StateOf(RunNode node)
    {
        return node.Kind switch
        {
            SimpleNodeKind.Ema => new EmaState(),
            SimpleNodeKind.Sma or SimpleNodeKind.StdDev => new WindowState(),
            SimpleNodeKind.Rsi => new RsiState(),
            SimpleNodeKind.Previous => new PreviousState(),
            SimpleNodeKind.Cross => new CrossState(),
            _ => new object()
        };
    }

    /// <exception cref="GraphValidationException">A parameter value is unknown or breaks the parameter's limits.</exception>
    public static StrategyRun Start(
        StrategyDefinition definition,
        CompositeCatalog catalog,
        IReadOnlyDictionary<string, NodeValue> parameterValues,
        TradingConditions conditions)
    {
        return Create(definition, catalog, parameterValues, conditions);
    }

    /// <summary>
    ///     A run that goes on from <paramref name="snapshot" />: its position, its active orders, each owned by the node of its path,
    ///     and its order numbers. The cash is the balance with what the positions made and less what the current one holds.
    /// </summary>
    /// <exception cref="GraphValidationException">A parameter value is unknown or breaks the parameter's limits.</exception>
    public static StrategyRun Restore(
        StrategyDefinition definition,
        CompositeCatalog catalog,
        IReadOnlyDictionary<string, NodeValue> parameterValues,
        TradingConditions conditions,
        RunSnapshot snapshot)
    {
        StrategyRun run = Create(definition, catalog, parameterValues, conditions);
        run._position = Execution.Position.Restore(snapshot, definition.Direction, conditions.Rules.MinQuantity);
        run._pastPnl = snapshot.PastPnl;
        run._lastOrderNumber = snapshot.LastOrderNumber;
        run.SkippedOrders = snapshot.SkippedOrders;
        decimal held = definition.Direction == StrategyDirection.Long ? snapshot.Invested : 2m * snapshot.Collateral - snapshot.Invested;
        run._cash = conditions.Balance + snapshot.PastPnl + snapshot.RealizedPnl - held;

        foreach (RunOrder order in snapshot.Orders)
        {
            int? node = order.NodePath is null ? null : run._indexByPath[order.NodePath];
            run._orders[order.Id] = new ActiveOrder(node, order.Side, order.Price, order.Remaining, order.Cancelling, order.Reserve, order.Filled);
        }

        return run;
    }

    /// <summary>What <see cref="Restore" /> needs to go on from here.</summary>
    public RunSnapshot Snapshot()
    {
        return new RunSnapshot(
            _position.Number,
            _position.Quantity,
            _position.Invested,
            _position.Collateral,
            _pastPnl,
            _position.RealizedPnl,
            _position.Fees,
            _position.LastEntryPrice,
            _position.LastEntryAt,
            _position.Status == PositionStatus.Closing,
            SkippedOrders,
            [
                .. _orders.Select(order => new RunOrder(
                    order.Key,
                    order.Value.Node is { } node ? _nodes[node].Path : null,
                    order.Value.Side,
                    order.Value.Price,
                    order.Value.Remaining,
                    order.Value.Cancelling,
                    order.Value.Reserve,
                    order.Value.Filled))
            ],
            _lastOrderNumber);
    }

    private static StrategyRun Create(
        StrategyDefinition definition,
        CompositeCatalog catalog,
        IReadOnlyDictionary<string, NodeValue> parameterValues,
        TradingConditions conditions)
    {
        IReadOnlyList<(string Parameter, string Reason)> errors = ParameterValueRules.Check(definition.Parameters, parameterValues);

        if (errors.Count > 0)
        {
            throw new GraphValidationException([.. errors.Select(error => new GraphError("", error.Parameter, error.Reason))]);
        }

        var parameters = definition.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameterValues.GetValueOrDefault(parameter.Name, parameter.Default));

        return new StrategyRun(GraphExpander.Expand(definition, catalog), parameters, definition.Direction, conditions);
    }

    public IReadOnlyList<OrderCommand> Handle(StrategyEvent @event)
    {
        _event = @event;
        _commands.Clear();
        _closed = null;
        Apply(@event);
        CloseWhenDone();

        for (int index = 0; index < _nodes.Count; index++)
        {
            Evaluate(index, _outputs[index], _states[index]);
        }

        return [.. _commands];
    }

    /// <summary>
    ///     Gives the indicators a closed bar of the history before the run goes live: the graph is evaluated without its order
    ///     nodes, so nothing is ordered and the position stays as it is.
    /// </summary>
    public void WarmUp(BarClosed bar)
    {
        _event = bar;
        _lastBar = bar.Bar;

        for (int index = 0; index < _nodes.Count; index++)
        {
            if (_nodes[index].Kind is not (SimpleNodeKind.MarketOrder or SimpleNodeKind.ClosePosition or SimpleNodeKind.HoldLimit))
            {
                Evaluate(index, _outputs[index], _states[index]);
            }
        }
    }

    /// <summary>
    ///     Closes the current position on the owner's request, as a full exit does: its orders are cancelled and what it holds is
    ///     sold at the market by the position itself.
    /// </summary>
    public IReadOnlyList<OrderCommand> Close(DateTimeOffset at)
    {
        _event = new RunStarted(at);
        _commands.Clear();
        _closed = null;
        _position.BeginClosing();
        CancelAll();
        SellWhatCameWhileClosing();
        CloseWhenDone();

        return [.. _commands];
    }

    /// <summary>
    ///     Keeps the books of a paused strategy: a fill or a cancel changes the position and its own rules work (a closing position
    ///     cancels what is left and sells what came meanwhile), but no node is evaluated.
    /// </summary>
    public IReadOnlyList<OrderCommand> Record(StrategyEvent @event)
    {
        _event = @event;
        _commands.Clear();
        _closed = null;
        Apply(@event);
        CloseWhenDone();

        return [.. _commands];
    }

    /// <summary>How many times an order node did not place an entry because free money or the exchange minimum did not allow it.</summary>
    public long SkippedOrders { get; private set; }

    /// <summary>The number of the current position, without building <see cref="State" />.</summary>
    public int Position => _position.Number;

    public RunState State => new RunState(
        _direction,
        _cash,
        _position.Quantity,
        _position.Invested,
        _position.Collateral,
        _cash - Free(),
        SkippedOrders,
        _position.Number,
        _position.Status,
        _position.RealizedPnl,
        _position.Fees,
        _closed);

    /// <summary>Value of an output after the last event; <c>null</c> when it has no value.</summary>
    public NodeValue? ValueOf(string path, string pin)
    {
        return _outputs[_indexByPath[path]].GetValueOrDefault(pin);
    }

    private void Apply(StrategyEvent @event)
    {
        switch (@event)
        {
            case BarClosed closed:
                _lastBar = closed.Bar;

                break;
            case OrderFilled filled:
                Fill(filled);

                break;
            case OrderCancelled cancelled:
                _orders.Remove(cancelled.OrderId);

                break;
            case OrderRejected rejected:
                _orders.Remove(rejected.OrderId);

                break;
            case OrderCancelledByVenue cancelledByVenue:
                // An exit with no node belongs to the closing position: what the venue cancelled of it is sold again.
                if (_orders.Remove(cancelledByVenue.OrderId, out ActiveOrder? removed) && removed.Node is null)
                {
                    SellWhatCameWhileClosing();
                }

                break;
        }
    }

    private void Fill(OrderFilled filled)
    {
        ActiveOrder order = _orders[filled.OrderId];
        _orders[filled.OrderId] = order with { Remaining = order.Remaining - filled.Quantity, Filled = true };

        if (order.Side == _entrySide)
        {
            _cash += _position.Enter(filled.Quantity, filled.Price, filled.Fee, filled.FeeAsset, filled.At);
        }
        else
        {
            _cash += _position.Exit(filled.Quantity, filled.Price, filled.Fee);
        }

        if (filled.IsComplete)
        {
            _orders.Remove(filled.OrderId);
        }

        if (_position.Status == PositionStatus.Closing)
        {
            CancelAll();
            SellWhatCameWhileClosing();
        }
    }

    /// <summary>
    ///     A fill that came while the position was closing is sold at the market by the position itself: what its own sells placed so far
    ///     do not cover, when the exchange lets it be sold.
    /// </summary>
    private void SellWhatCameWhileClosing()
    {
        decimal selling = _orders.Values.Where(order => order.Node is null).Sum(order => order.Remaining!.Value);
        decimal quantity = Down(_position.Quantity - selling, _rules.QuantityStep);

        if (!Sellable(quantity))
        {
            return;
        }

        string id = $"o-{++_lastOrderNumber}";
        OrderSide side = _entrySide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        _orders[id] = new ActiveOrder(null, side, Remaining: quantity);
        _commands.Add(new PlaceOrder(id, null, side, OrderType.Market, null, quantity, null));
    }

    private bool Sellable(decimal quantity)
    {
        return quantity > 0m && quantity >= _rules.MinQuantity;
    }

    /// <summary>
    ///     A closing position with no order left is closed, and the next position becomes the current one; what it could still sell has
    ///     a sell of its own among the orders.
    /// </summary>
    private void CloseWhenDone()
    {
        if (_position.Status == PositionStatus.Closing && _orders.Count == 0)
        {
            _closed = new ClosedPosition(_position.Number, _event.At, _position.RealizedPnl, _position.Fees);
            _pastPnl += _position.RealizedPnl;
            _position = _position.Next();
        }
    }

    /// <summary>A closing position cancels every order of its nodes that is not being cancelled yet; its own sells stay.</summary>
    private void CancelAll()
    {
        foreach ((string id, ActiveOrder order) in _orders.Where(order => order.Value.Node is not null && !order.Value.Cancelling).ToArray())
        {
            _orders[id] = order with { Cancelling = true };
            _commands.Add(new CancelOrder(id));
        }
    }

    private void Evaluate(int index, Dictionary<string, NodeValue?> outputs, object state)
    {
        RunNode node = _nodes[index];

        switch (node.Kind)
        {
            case SimpleNodeKind.Parameters:
                foreach ((string name, NodeValue value) in _parameters)
                {
                    outputs[name] = value;
                }

                break;
            case SimpleNodeKind.Inputs or SimpleNodeKind.Outputs:
                foreach (string pin in node.Inputs.Keys)
                {
                    outputs[pin] = Input(node, pin);
                }

                break;
            case SimpleNodeKind.Candle:
                outputs["open"] = Number(_lastBar?.Open);
                outputs["high"] = Number(_lastBar?.High);
                outputs["low"] = Number(_lastBar?.Low);
                outputs["close"] = Number(_lastBar?.Close);
                outputs["volume"] = Number(_lastBar?.Volume);

                break;
            case SimpleNodeKind.Position:
                bool open = IsOpen();
                outputs["hasPosition"] = new BooleanValue(open);
                outputs["quantity"] = new NumberValue(open ? _position.Quantity : 0m);
                outputs["averagePrice"] = Number(open ? _position.AveragePrice : null);
                outputs["invested"] = new NumberValue(open ? _position.Invested : 0m);
                outputs["lastEntryPrice"] = Number(open ? _position.LastEntryPrice : null);
                outputs["sinceLastEntry"] = open && _position.LastEntryAt is { } at ? new DurationValue(_event.At - at) : null;
                outputs["free"] = new NumberValue(Free());

                break;
            case SimpleNodeKind.Ema:
                outputs["value"] = Indicator(node, (EmaState)state, ema => ema.Value);

                break;
            case SimpleNodeKind.Sma:
                outputs["value"] = Indicator(node, (WindowState)state, (window, period) => window.Average(period));

                break;
            case SimpleNodeKind.StdDev:
                outputs["value"] = Indicator(node, (WindowState)state, (window, period) => window.Deviation(period));

                break;
            case SimpleNodeKind.Rsi:
                outputs["value"] = Indicator(node, (RsiState)state, rsi => rsi.Value);

                break;
            case SimpleNodeKind.Previous:
                outputs["value"] = Previous(node, (PreviousState)state);

                break;
            case SimpleNodeKind.Cross:
                (outputs["up"], outputs["down"]) = Cross(node, (CrossState)state);

                break;
            case SimpleNodeKind.Add or SimpleNodeKind.Subtract or SimpleNodeKind.Multiply or SimpleNodeKind.Divide:
                outputs["result"] = NodeFunctions.Arithmetic(node.Kind, Input(node, "a"), Input(node, "b"));

                break;
            case SimpleNodeKind.Compare:
                outputs["result"] = new BooleanValue(NodeFunctions.Compare(node.Settings.Operation!.Value, Input(node, "a"), Input(node, "b")));

                break;
            case SimpleNodeKind.And:
                outputs["result"] = new BooleanValue(IsTrue(node, "a") && IsTrue(node, "b"));

                break;
            case SimpleNodeKind.Or:
                outputs["result"] = new BooleanValue(IsTrue(node, "a") || IsTrue(node, "b"));

                break;
            case SimpleNodeKind.Not:
                outputs["result"] = new BooleanValue(!IsTrue(node, "value"));

                break;
            case SimpleNodeKind.MarketOrder:
                MarketOrder(index, node);

                break;
            case SimpleNodeKind.ClosePosition:
                ClosePosition(index, node);

                break;
            case SimpleNodeKind.HoldLimit:
                HoldLimit(index, node);

                break;
        }
    }

    /// <summary>A market order goes out when the condition holds and the node has no unfinished order.</summary>
    private void MarketOrder(int index, RunNode node)
    {
        if (HasOrder(index) || !IsTrue(node, "when") || Positive(node, "size") is not { } size)
        {
            return;
        }

        OrderSide side = node.Settings.Side!.Value;
        bool byAmount = node.Settings.SizeMode == OrderSizeMode.Amount;

        if (side == _entrySide)
        {
            Place(index, node, side, byAmount ? null : Down(size, _rules.QuantityStep), byAmount ? size : null);

            return;
        }

        decimal? quantity = !byAmount ? size : _lastBar is { Close: > 0m } bar ? size / bar.Close : null;

        if (IsOpen() && quantity is { } wanted)
        {
            Place(index, node, side, Math.Min(Down(wanted, _rules.QuantityStep), ExitableQuantity()), null);
        }
    }

    private void ClosePosition(int index, RunNode node)
    {
        if (!HasOrder(index) && IsTrue(node, "when") && IsOpen())
        {
            Place(index, node, _entrySide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy, ExitableQuantity(), null);
        }
    }

    /// <summary>
    ///     Keeps one limit order with the wanted price and unfilled quantity; a different one is cancelled first and replaced
    ///     only once the cancel is confirmed, so the old order never holds the balance the new one needs. An order with a fill is
    ///     not replaced for its size: its remainder waits for the rest of the fill rather than a new order of the whole size.
    /// </summary>
    private void HoldLimit(int index, RunNode node)
    {
        KeyValuePair<string, ActiveOrder> existing = _orders.FirstOrDefault(order => order.Value.Node == index);

        if (existing.Value is { Cancelling: true })
        {
            return;
        }

        OrderSide side = node.Settings.Side!.Value;
        (decimal Price, decimal Quantity)? wanted = WantedLimit(node, side);

        if (existing.Value is null)
        {
            if (wanted is { } order)
            {
                Place(index, node, side, OrderType.Limit, order.Price, order.Quantity, null);
            }

            return;
        }

        if (wanted is not { } same || same.Price != existing.Value.Price || (!existing.Value.Filled && same.Quantity != existing.Value.Remaining))
        {
            _orders[existing.Key] = existing.Value with { Cancelling = true };
            _commands.Add(new CancelOrder(existing.Key));
        }
    }

    /// <summary>
    ///     The order on the steps of the instrument: a buy price rounds down, a sell price up, a quantity down. A price that rounds
    ///     to zero counts as a missing value.
    /// </summary>
    private (decimal Price, decimal Quantity)? WantedLimit(RunNode node, OrderSide side)
    {
        if (!IsTrue(node, "when") || Positive(node, "price") is not { } wantedPrice || Positive(node, "size") is not { } size)
        {
            return null;
        }

        decimal price = side == OrderSide.Buy ? Down(wantedPrice, _rules.TickSize) : Up(wantedPrice, _rules.TickSize);

        // A buy below one tick rounds down to no price at all.
        if (price == 0m)
        {
            return null;
        }
        decimal quantity = Down(node.Settings.SizeMode == OrderSizeMode.Amount ? size / price : size, _rules.QuantityStep);

        if (side == _entrySide)
        {
            return (price, quantity);
        }

        return IsOpen() ? (price, Math.Min(quantity, ExitableQuantity())) : null;
    }

    private bool IsOpen()
    {
        return _position.Status == PositionStatus.Open;
    }

    /// <summary>An exit sells at most the position rounded down to the quantity step.</summary>
    private decimal ExitableQuantity()
    {
        return Down(_position.Quantity, _rules.QuantityStep);
    }

    // The remainder never overflows, unlike a division by a fine step.
    private static decimal Down(decimal value, decimal step)
    {
        return value - value % step;
    }

    private static decimal Up(decimal value, decimal step)
    {
        decimal rest = value % step;

        return rest == 0m ? value : value - rest + step;
    }

    /// <summary>Value of an entry with its fee in the quote asset.</summary>
    private decimal? WithFee(OrderType type, decimal? value)
    {
        decimal rate = _fees.AssetOf(_entrySide) == FeeAsset.Base ? 0m : type == OrderType.Limit ? _fees.LimitRate : _fees.MarketRate;

        return value * (1m + rate);
    }

    /// <summary>
    ///     The cash less what unfinished entries hold back: a standing limit entry its value and the fee in the quote asset, an
    ///     unfilled market entry its value when it was placed with that fee, so several entries of one event never spend more.
    /// </summary>
    private decimal Free()
    {
        decimal rate = _fees.AssetOf(_entrySide) == FeeAsset.Quote ? _fees.LimitRate : 0m;
        decimal reserved = _orders.Values
            .Where(order => order.Side == _entrySide && order.Price is not null)
            .Sum(order => order.Price!.Value * order.Remaining!.Value * (1m + rate));

        return _cash - reserved - _orders.Values.Sum(order => order.Reserve);
    }

    private bool HasOrder(int index)
    {
        return _orders.Values.Any(order => order.Node == index);
    }

    private void Place(int index, RunNode node, OrderSide side, decimal? quantity, decimal? amount)
    {
        Place(index, node, side, OrderType.Market, null, quantity, amount);
    }

    /// <summary>
    ///     An order below the exchange minimum, or an entry that free money does not pay for, is skipped as if its condition were false,
    ///     and counted. A value that cannot be known yet, of a market order by quantity before the first bar, passes both checks.
    /// </summary>
    private void Place(int index, RunNode node, OrderSide side, OrderType type, decimal? price, decimal? quantity, decimal? amount)
    {
        // Nodes wait until the closing position is closed; what they place then belongs to the next one.
        if (_position.Status == PositionStatus.Closing)
        {
            return;
        }

        decimal? value = amount ?? quantity * (price ?? _lastBar?.Close);

        if (quantity < _rules.MinQuantity || value < _rules.MinAmount || (side == _entrySide && WithFee(type, value) > Free()))
        {
            SkippedOrders++;

            return;
        }

        string id = $"o-{++_lastOrderNumber}";
        decimal reserve = type == OrderType.Market && side == _entrySide ? WithFee(type, value) ?? 0m : 0m;
        _orders[id] = new ActiveOrder(index, side, price, type == OrderType.Limit ? quantity : null, Reserve: reserve);
        _commands.Add(new PlaceOrder(id, node.Path, side, type, price, quantity, amount));
    }

    /// <summary>Only a closed bar with a value moves an indicator; a period below one gives no value.</summary>
    private NumberValue? Indicator<TState>(RunNode node, TState state, Func<TState, decimal?> read)
        where TState : IIndicatorState
    {
        return Indicator(node, state, (_, _) => read(state));
    }

    private NumberValue? Indicator<TState>(RunNode node, TState state, Func<TState, decimal, decimal?> read)
        where TState : IIndicatorState
    {
        if (Input(node, "period") is not NumberValue { Value: >= 1m and var period })
        {
            return null;
        }

        if (_event is BarClosed && Input(node, "source") is NumberValue { Value: var source })
        {
            state.Add(source, period);
        }

        return Number(read(state, period));
    }

    /// <summary>Only a closed bar moves the node on, whether its input has a value or not.</summary>
    private NumberValue? Previous(RunNode node, PreviousState state)
    {
        if (_event is BarClosed)
        {
            state.Close((Input(node, "value") as NumberValue)?.Value);
        }

        return Number(state.Previous);
    }

    /// <summary>A cross is a signal of the closed bar it happened on; every other event sees no cross.</summary>
    private (BooleanValue Up, BooleanValue Down) Cross(RunNode node, CrossState state)
    {
        if (_event is not BarClosed)
        {
            return (new BooleanValue(false), new BooleanValue(false));
        }

        decimal? a = (Input(node, "a") as NumberValue)?.Value;
        decimal? b = (Input(node, "b") as NumberValue)?.Value;
        bool up = state.PreviousA <= state.PreviousB && a > b;
        bool down = state.PreviousA >= state.PreviousB && a < b;
        state.PreviousA = a;
        state.PreviousB = b;

        return (new BooleanValue(up), new BooleanValue(down));
    }

    private NodeValue? Input(RunNode node, string pin)
    {
        Source source = node.Inputs[pin];

        return source.Literal ?? _outputs[source.Node].GetValueOrDefault(source.Pin);
    }

    /// <summary>An order price or size that is zero or negative cannot be ordered, so it counts as a missing value.</summary>
    private decimal? Positive(RunNode node, string pin)
    {
        return Input(node, pin) is NumberValue { Value: > 0m and var value } ? value : null;
    }

    private bool IsTrue(RunNode node, string pin)
    {
        return Input(node, pin) is BooleanValue { Value: true };
    }

    private static NumberValue? Number(decimal? value)
    {
        return value is { } number ? new NumberValue(number) : null;
    }
}