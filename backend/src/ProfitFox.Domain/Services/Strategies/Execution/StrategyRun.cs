using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.Validation;
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
    private readonly PositionBook _position = new PositionBook();
    private readonly OrderSide _entrySide;
    private readonly List<OrderCommand> _commands = [];
    private int _lastOrderNumber;
    private StrategyEvent _event = null!;
    private Bar? _lastBar;

    private StrategyRun(IReadOnlyList<RunNode> nodes, IReadOnlyDictionary<string, NodeValue> parameters, StrategyDirection direction)
    {
        _entrySide = direction == StrategyDirection.Long ? OrderSide.Buy : OrderSide.Sell;
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
            SimpleNodeKind.Cross => new CrossState(),
            _ => new object()
        };
    }

    /// <exception cref="GraphValidationException">A parameter value is unknown or breaks the parameter's limits.</exception>
    public static StrategyRun Start(StrategyDefinition definition, CompositeCatalog catalog, IReadOnlyDictionary<string, NodeValue> parameterValues)
    {
        IReadOnlyList<(string Parameter, string Reason)> errors = ParameterValues.Check(definition.Parameters, parameterValues);

        if (errors.Count > 0)
        {
            throw new GraphValidationException([.. errors.Select(error => new GraphError("", error.Parameter, error.Reason))]);
        }

        var parameters = definition.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameterValues.GetValueOrDefault(parameter.Name, parameter.Default));

        return new StrategyRun(GraphExpander.Expand(definition, catalog), parameters, definition.Direction);
    }

    public IReadOnlyList<OrderCommand> Handle(StrategyEvent @event)
    {
        _event = @event;
        _commands.Clear();
        Apply(@event);

        for (int index = 0; index < _nodes.Count; index++)
        {
            Evaluate(index, _outputs[index], _states[index]);
        }

        return [.. _commands];
    }

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
        }
    }

    private void Fill(OrderFilled filled)
    {
        ActiveOrder order = _orders[filled.OrderId];
        _orders[filled.OrderId] = order with { Remaining = order.Remaining - filled.Quantity };

        if (order.Side == _entrySide)
        {
            _position.Enter(filled.Quantity, filled.Price, filled.At);
        }
        else
        {
            _position.Exit(filled.Quantity);
        }

        if (filled.IsComplete)
        {
            _orders.Remove(filled.OrderId);
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
                outputs["hasPosition"] = new BooleanValue(_position.Quantity > 0m);
                outputs["quantity"] = new NumberValue(_position.Quantity);
                outputs["averagePrice"] = Number(_position.AveragePrice);
                outputs["invested"] = new NumberValue(_position.Invested);
                outputs["lastEntryPrice"] = Number(_position.LastEntryPrice);
                outputs["sinceLastEntry"] = _position.LastEntryAt is { } at ? new DurationValue(_event.At - at) : null;

                break;
            case SimpleNodeKind.Ema:
                outputs["value"] = Ema(node, (EmaState)state);

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
            Place(index, node, side, byAmount ? null : size, byAmount ? size : null);

            return;
        }

        decimal? quantity = !byAmount ? size : _lastBar is { Close: > 0m } bar ? size / bar.Close : null;

        if (_position.Quantity > 0m && quantity is { } wanted)
        {
            Place(index, node, side, Math.Min(wanted, _position.Quantity), null);
        }
    }

    private void ClosePosition(int index, RunNode node)
    {
        if (!HasOrder(index) && IsTrue(node, "when") && _position.Quantity > 0m)
        {
            Place(index, node, _entrySide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy, _position.Quantity, null);
        }
    }

    /// <summary>
    ///     Keeps one limit order with the wanted price and unfilled quantity; a different one is cancelled first and replaced
    ///     only once the cancel is confirmed, so the old order never holds the balance the new one needs.
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

        if (wanted is not { } same || same.Price != existing.Value.Price || same.Quantity != existing.Value.Remaining)
        {
            _orders[existing.Key] = existing.Value with { Cancelling = true };
            _commands.Add(new CancelOrder(existing.Key));
        }
    }

    private (decimal Price, decimal Quantity)? WantedLimit(RunNode node, OrderSide side)
    {
        if (!IsTrue(node, "when") || Positive(node, "price") is not { } price || Positive(node, "size") is not { } size)
        {
            return null;
        }

        decimal quantity = node.Settings.SizeMode == OrderSizeMode.Amount ? size / price : size;

        if (side == _entrySide)
        {
            return (price, quantity);
        }

        return _position.Quantity > 0m ? (price, Math.Min(quantity, _position.Quantity)) : null;
    }

    private bool HasOrder(int index)
    {
        return _orders.Values.Any(order => order.Node == index);
    }

    private void Place(int index, RunNode node, OrderSide side, decimal? quantity, decimal? amount)
    {
        Place(index, node, side, OrderType.Market, null, quantity, amount);
    }

    private void Place(int index, RunNode node, OrderSide side, OrderType type, decimal? price, decimal? quantity, decimal? amount)
    {
        string id = $"o-{++_lastOrderNumber}";
        _orders[id] = new ActiveOrder(index, side, price, type == OrderType.Limit ? quantity : null);
        _commands.Add(new PlaceOrder(id, node.Path, side, type, price, quantity, amount));
    }

    /// <summary>Only a closed bar moves the average; a period below one gives no value.</summary>
    private NumberValue? Ema(RunNode node, EmaState state)
    {
        if (Input(node, "period") is not NumberValue { Value: >= 1m and var period })
        {
            return null;
        }

        if (_event is BarClosed && Input(node, "source") is NumberValue { Value: var source })
        {
            state.Add(source, period);
        }

        return Number(state.Value);
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