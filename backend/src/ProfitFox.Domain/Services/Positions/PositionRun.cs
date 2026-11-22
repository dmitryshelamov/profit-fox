using System.Globalization;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Positions;

/// <summary>
///     A strategy with orders on one pair (change add-position-node, design.md, decision 6): the graph at every close and
///     again after every fill, the orders of its nodes, their fills minute by minute, and the cycles of the position, whose
///     fills and result <see cref="CycleRun" /> keeps. The graph runs on every candle from the first; orders are taken from
///     <c>from</c> on, where the position is closed, and nothing is known after <c>end</c>, where the computed history ends.
///     A cancelled token stops the run at its next minute or fill.
///     <list type="number">
///         <item>An order placed at a close takes part from the minute of the close; one placed after a fill, from the next.</item>
///         <item>
///             In a minute, fills go in the order worst for the position: the stop, then what increases it, then what
///             decreases it and the target; with the position closed a buy goes before a sale. After each the graph is
///             computed again; an order it leaves as it was can fill in the same minute.
///         </item>
///         <item>
///             A cycle is ambiguous when orders of different actions could fill in one of its minutes. This is looked at
///             before each fill of the minute, with what the fills of limit orders already did to the cycle in it: a fill by
///             the market is at the open and so before the rest, as the entry of a trade of a plan.
///         </item>
///         <item>The stop, the target and the time close the whole position and take off the other orders.</item>
///         <item>
///             The time of a cycle an order of <c>enter</c> opened runs from the moment of the order, that of a cycle a
///             <c>market-order</c> or a <c>limit-order</c> opened from the fill that opened it. A time that ran out closes the
///             cycle before anything else at that moment: before the graph of a close, before a hole in the history, and at
///             the end of the history. An order of <c>enter</c> that met no minute with trades within its time gives no trade.
///         </item>
///         <item>An order whose cycle would end by its time after the end of the history gives no cycle: the reason is the period.</item>
///         <item>
///             An order of <c>market-order</c> or <c>limit-order</c> that cannot open a cycle — by the values of the exit
///             nodes or by the period — is refused at most once of a node in a candle, however many minutes it would fill in.
///         </item>
///         <item>An order of <c>close-position</c> is of the position it was placed at: it is taken off when that position closes.</item>
///         <item>A minute the history does not know, while a cycle is open or an order stands, leaves the cycle out on coverage.</item>
///     </list>
/// </summary>
public sealed class PositionRun
{
    // A runaway graph that fills and places orders without end in one minute is a defect, not a strategy.
    private const int MaxFillsInMinute = 10_000;

    private readonly MinutePath _path;
    private readonly TradeCosts _costs;
    private readonly GraphRun _run;
    private readonly OrderNode[] _nodes;
    private readonly InputSource? _stop;
    private readonly InputSource? _target;
    private readonly InputSource? _after;
    private readonly Dictionary<string, Held> _standing = new(StringComparer.Ordinal);
    private readonly List<CycleOutcome?> _cycles = [];
    private readonly Dictionary<string, DateTimeOffset> _entered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _refused = new(StringComparer.Ordinal);
    private readonly List<OrderOutcome> _orders = [];
    private readonly long _from;
    private readonly DateTimeOffset _end;
    private readonly CancellationToken _cancellation;
    private CycleRun? _cycle;
    private long _minute;
    private double _lastClose = double.NaN;
    private bool _computed;
    private bool _afterHole;
    private bool _limitIncrease;
    private bool _limitDecrease;
    private DateTimeOffset _close;

    public PositionRun(TradingStrategy strategy, MinutePath path, TradeCosts costs, DateTimeOffset from, DateTimeOffset? end = null, CancellationToken cancellationToken = default)
    {
        _cancellation = cancellationToken;
        _path = path;
        _end = end ?? path.Start + TimeSpan.FromMinutes(path.Minutes);
        _costs = costs;
        var graph = CompiledGraph.Compile(strategy.Graph);
        _run = new GraphRun(graph);
        _nodes = [.. graph.Nodes.Where(node => node.Definition.Kind is NodeCatalog.Enter or NodeCatalog.MarketOrder or NodeCatalog.LimitOrder or NodeCatalog.ClosePosition)
            .Select(node => OrderNode.Of(graph, node))];
        _stop = Input(graph, NodeCatalog.StopLoss, NodeCatalog.Distance);
        _target = Input(graph, NodeCatalog.TakeProfit, NodeCatalog.Distance);
        _after = Input(graph, NodeCatalog.CloseAfter, NodeCatalog.After);
        _from = path.MinuteIndex(from);
        _minute = _from;
    }

    /// <summary>
    ///     The orders that stand now, in the order of their nodes.
    /// </summary>
    public IReadOnlyList<StandingOrder> Standing => [.. _nodes.Where(node => _standing.ContainsKey(node.Id)).Select(node => _standing[node.Id].Order)];

    /// <summary>
    ///     The candle closed: the minutes up to its close are walked with the orders that stood, a time that ran out at the
    ///     close closes its cycle, then the graph is computed at the close and the orders of its nodes are taken. Candles go
    ///     in the order of their close.
    /// </summary>
    public void Step(ClosedCandle candle, FundingPayment? lastFunding)
    {
        Advance(candle.CloseTime);
        Expire(candle.CloseTime);
        _run.Step(candle, lastFunding, State());
        _close = candle.CloseTime;
        if (_path.MinuteIndex(candle.CloseTime) < _from)
        {
            return;
        }

        _computed = true;
        Take(candle.CloseTime, _path.MinuteIndex(candle.CloseTime));
    }

    /// <summary>
    ///     Walks the minutes before <paramref name="until" />.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token of the run was cancelled.</exception>
    public void Advance(DateTimeOffset until)
    {
        long end = _path.MinuteIndex(until);
        for (; _minute < end; _minute++)
        {
            _cancellation.ThrowIfCancellationRequested();
            Minute(_minute);
        }
    }

    /// <summary>
    ///     The minutes before <paramref name="end" /> walked; a cycle whose time ran out at the end is closed by it, a cycle
    ///     still open is an estimate at the last close, and an entry order still waiting gave no cycle before the end.
    /// </summary>
    public PositionResult Finish(DateTimeOffset end)
    {
        Advance(end);
        Expire(end);
        if (_cycle is { } open)
        {
            _cycles[^1] = open.Estimate(end, _lastClose);
            _cycle = null;
        }

        foreach (Held held in _standing.Values.Where(held => held.Kind == OrderKind.Enter))
        {
            _orders.Add(Outcome(held.Kind, held.Order.Placed, held.Order.Node, held.Order.Side, OrderResult.Excluded, OrderExclusion.Period, null, null, null));
        }

        _standing.Clear();
        return new PositionResult([.. _cycles.Select(cycle => cycle!)], [.. _orders.OrderBy(order => order.Moment)]);
    }

    private void Minute(long minute)
    {
        DateTimeOffset time = _path.Start + TimeSpan.FromMinutes(minute);

        // The time is looked at before the hole (spec strategy-position «Срок у края истории»). The orders of the graph
        // computed at this moment take part from this minute, as those of a close.
        if (Expire(time))
        {
            Again(time, minute);
        }

        bool covered = minute >= 0 && minute < _path.Minutes && _path.Covered((int)minute);
        if (!covered)
        {
            if (_cycle is not null || _standing.Count > 0)
            {
                Hole();
            }

            return;
        }

        int index = (int)minute;
        if (_afterHole)
        {
            _afterHole = false;
            if (_computed)
            {
                Again(time, minute + 1);
            }
        }

        double open = _path.Open(index);
        if (double.IsNaN(open))
        {
            return;
        }

        double high = _path.High(index);
        double low = _path.Low(index);
        _limitIncrease = false;
        _limitDecrease = false;
        for (int fills = 0; Fill(minute, time, open, high, low); fills++)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (fills >= MaxFillsInMinute)
            {
                throw new InvalidOperationException($"The strategy fills orders without end in the minute {time:O}.");
            }
        }

        _lastClose = _path.Close(index);
    }

    // Whether orders of different actions could fill in the minute of the open cycle (spec strategy-position «Порядок
    // исполнений в минуте»): an increase and a decrease, or the stop and anything else, the target among the decreases as
    // in the trade of a plan. What could fill is what can fill now and what the fills of limit orders did to the cycle
    // earlier in this minute: a limit order fills somewhere inside the minute, a market order at its open. An order of
    // enter does nothing to an open position.
    private bool Ambiguous(Candidate[] candidates)
    {
        bool stop = candidates.Any(candidate => candidate.Kind == CandidateKind.Stop);
        bool target = candidates.Any(candidate => candidate.Kind == CandidateKind.Target);
        Held[] orders = [.. candidates.Where(candidate => candidate.Kind == CandidateKind.Order && candidate.Held!.Kind != OrderKind.Enter).Select(candidate => candidate.Held!)];
        bool increase = _limitIncrease || orders.Any(Increases);
        bool decrease = _limitDecrease || target || orders.Any(order => !Increases(order));
        return (stop && (increase || decrease)) || (increase && decrease);
    }

    // The first fill of the minute in the worst order, done; false when nothing more fills.
    private bool Fill(long minute, DateTimeOffset time, double open, double high, double low)
    {
        Candidate[] candidates = [.. Candidates(minute, high, low)];
        if (candidates.Length == 0)
        {
            return false;
        }

        if (_cycle is { } cycle && Ambiguous(candidates))
        {
            cycle.Ambiguous = true;
        }

        Candidate candidate = candidates.OrderBy(Priority).First();

        switch (candidate.Kind)
        {
            case CandidateKind.Stop:
            {
                CycleRun open1 = _cycle!;
                double raw = CycleRun.StopExit(open1.IsLong, open1.StopLevel, open);
                Close(open1, time, raw, raw * (1 - Sign(open1) * _costs.Slippage), CycleExit.Stop, node: null);
                break;
            }
            case CandidateKind.Target:
            {
                CycleRun open1 = _cycle!;
                double level = open1.TargetLevel;
                Close(open1, time, level, level, CycleExit.Target, node: null);
                break;
            }
            default:
                Order(candidate.Held!, time, open);
                break;
        }

        Again(time, minute + 1);
        return true;
    }

    private IEnumerable<Candidate> Candidates(long minute, double high, double low)
    {
        if (_cycle is { } cycle)
        {
            if (cycle.Stop is not null && CycleRun.StopHit(cycle.IsLong, high, low, cycle.StopLevel))
            {
                yield return new Candidate(CandidateKind.Stop, null, 0);
            }

            if (cycle.Target is not null && CycleRun.TargetHit(cycle.IsLong, high, low, cycle.TargetLevel))
            {
                yield return new Candidate(CandidateKind.Target, null, 0);
            }
        }

        for (int index = 0; index < _nodes.Length; index++)
        {
            if (!_standing.TryGetValue(_nodes[index].Id, out Held? held) || held.ActiveFrom > minute)
            {
                continue;
            }

            bool reached = held.Order.Price is not { } price || (held.Order.Side == FillSide.Buy ? low <= price : high >= price);
            if (reached)
            {
                yield return new Candidate(CandidateKind.Order, held, index);
            }
        }
    }

    // Stop; then increases (market first); then decreases (market first) and the target; with no position, buys then sales.
    private int Priority(Candidate candidate)
    {
        const int Nodes = 1_000_000;
        return candidate.Kind switch
        {
            CandidateKind.Stop => 0,
            CandidateKind.Target => 4 * Nodes,
            _ when _cycle is null => (candidate.Held!.Order.Side == FillSide.Buy ? 1 : 3) * Nodes + Market(candidate) + candidate.Index,
            _ => (Increases(candidate.Held!) ? 1 : 3) * Nodes + Market(candidate) + candidate.Index
        };

        static int Market(Candidate candidate)
        {
            return candidate.Held!.Order.Price is null ? 0 : Nodes / 2;
        }
    }

    private bool Increases(Held held)
    {
        return _cycle is { } cycle && held.Order.Side == (cycle.IsLong ? FillSide.Buy : FillSide.Sell);
    }

    private void Order(Held held, DateTimeOffset time, double open)
    {
        StandingOrder order = held.Order;
        _standing.Remove(order.Node);
        double raw = order.Price ?? open;
        double fill = order.Price ?? CycleRun.MarketFill(open, order.Side, _costs);
        if (_cycle is not { } cycle)
        {
            if (held.Kind == OrderKind.ClosePosition)
            {
                return;
            }

            if (!held.Exits.Valid)
            {
                Refuse(held, time, OrderExclusion.Plan);
                return;
            }

            // The time runs from the moment of an order of enter and from the fill of the other orders (spec
            // strategy-position «Стоп, цель и срок при нескольких входах»).
            DateTimeOffset? until = held.Exits.After is { } after ? (held.Kind == OrderKind.Enter ? order.Placed : time) + after : null;
            if (until > _end)
            {
                Refuse(held, time, OrderExclusion.Period);
                return;
            }

            double amount = order.SizeInQuantity ? order.Size * fill : order.Size;
            EntrySide side = order.Side == FillSide.Buy ? EntrySide.Long : EntrySide.Short;
            _cycle = CycleRun.Open(_path, _costs, side, time, raw, fill, amount, order.Node, held.Exits.Stop, held.Exits.Target, until, maker: held.Kind == OrderKind.Limit);
            _limitIncrease = held.Kind == OrderKind.Limit;
            _limitDecrease = false;
            _cycles.Add(null);
            _orders.Add(Outcome(held.Kind, time, order.Node, order.Side, OrderResult.Filled, null, fill, amount, _cycles.Count - 1));
            return;
        }

        if (held.Kind == OrderKind.Enter)
        {
            _orders.Add(Outcome(held.Kind, order.Placed, order.Node, order.Side, OrderResult.Skipped, null, null, null, null));
            return;
        }

        if (Increases(held))
        {
            double amount = order.SizeInQuantity ? order.Size * fill : order.Size;
            cycle.Increase(time, raw, fill, amount, order.Node, maker: held.Kind == OrderKind.Limit);
            _limitIncrease |= held.Kind == OrderKind.Limit;
            _orders.Add(Outcome(held.Kind, time, order.Node, order.Side, OrderResult.Filled, null, fill, amount, _cycles.Count - 1));
            return;
        }

        double quantity = held.Kind == OrderKind.ClosePosition ? cycle.Quantity : order.SizeInQuantity ? order.Size : order.Size / fill;
        CycleExit reason = held.Kind switch
        {
            OrderKind.ClosePosition => CycleExit.ClosePosition,
            OrderKind.Limit => CycleExit.LimitOrder,
            _ => CycleExit.MarketOrder
        };
        double sold = cycle.Reduce(time, raw, fill, quantity, reason, order.Node, maker: held.Kind == OrderKind.Limit);
        _limitDecrease |= held.Kind == OrderKind.Limit;
        _orders.Add(Outcome(held.Kind, time, order.Node, order.Side, OrderResult.Filled, null, fill, sold * fill, _cycles.Count - 1));
        if (cycle.Closed)
        {
            Finished(cycle);
        }
    }

    // An order that would have opened a cycle and cannot (spec strategy-position «Недопустимые выходы у заявки по условию»):
    // while the reason stays, the node places the same order after every refusal, so one refusal of a node is written in
    // a candle and the rest of them are not.
    private void Refuse(Held held, DateTimeOffset time, OrderExclusion reason)
    {
        if (_refused.TryGetValue(held.Order.Node, out DateTimeOffset candle) && candle == _close)
        {
            return;
        }

        _refused[held.Order.Node] = _close;
        _orders.Add(Outcome(held.Kind, time, held.Order.Node, held.Order.Side, OrderResult.Excluded, reason, null, null, null));
    }

    // The stop, the target and the time close the whole position and take off the other orders.
    private void Close(CycleRun cycle, DateTimeOffset time, double raw, double fill, CycleExit reason, string? node)
    {
        cycle.Close(time, raw, fill, reason, node);
        Finished(cycle);
        CancelAll();
    }

    // What ran out by now. An order of enter that met no minute with trades within its time gives no trade, as the trade
    // of a plan (spec trade-plan-outcome). The time of the cycle closes it at the close of the last minute with trades,
    // at the moment the time ran out, and takes off the other orders; true when the cycle closed.
    private bool Expire(DateTimeOffset now)
    {
        if (_standing.Count > 0)
        {
            foreach (Held held in _standing.Values.Where(held => held.Kind == OrderKind.Enter && held.Exits.After is { } after && held.Order.Placed + after <= now).ToList())
            {
                _standing.Remove(held.Order.Node);
                _orders.Add(Outcome(held.Kind, held.Order.Placed, held.Order.Node, held.Order.Side, OrderResult.Excluded, OrderExclusion.NoTrade, null, null, null));
            }
        }

        if (_cycle is not { Until: { } until } cycle || until > now)
        {
            return false;
        }

        double fill = _lastClose * (1 - Sign(cycle) * _costs.Slippage);
        Close(cycle, until, _lastClose, fill, CycleExit.Time, node: null);
        return true;
    }

    // The cycle closed. An order of close-position not filled yet was of this position (spec strategy-execution «Закрытие
    // позиции по условию»): it is taken off, so it does not close the next cycle.
    private void Finished(CycleRun cycle)
    {
        _cycles[^1] = cycle.Outcome();
        _cycle = null;
        foreach (string node in _standing.Where(standing => standing.Value.Kind == OrderKind.ClosePosition).Select(standing => standing.Key).ToList())
        {
            _standing.Remove(node);
        }
    }

    private void Hole()
    {
        if (_cycle is { } cycle)
        {
            _cycles[^1] = CycleOutcome.Excluded(TradeStatus.Coverage, cycle.Side) with { Fills = [.. cycle.Fills] };
            _cycle = null;
        }

        foreach (Held held in _standing.Values.Where(held => held.Kind == OrderKind.Enter))
        {
            _orders.Add(Outcome(held.Kind, held.Order.Placed, held.Order.Node, held.Order.Side, OrderResult.Excluded, OrderExclusion.Coverage, null, null, null));
        }

        _standing.Clear();
        _afterHole = true;
    }

    private void CancelAll()
    {
        foreach (Held held in _standing.Values.Where(held => held.Kind == OrderKind.Enter).ToList())
        {
            _orders.Add(Outcome(held.Kind, held.Order.Placed, held.Order.Node, held.Order.Side, OrderResult.Skipped, null, null, null, null));
        }

        _standing.Clear();
    }

    private void Again(DateTimeOffset time, long activeFrom)
    {
        _run.Again(State(), time);
        Take(time, activeFrom);
    }

    // The orders of the nodes after a computation: enter, market-order and close-position place one order at a time;
    // limit-order keeps, replaces or takes off its one order.
    private void Take(DateTimeOffset moment, long activeFrom)
    {
        Exits exits = ReadExits();
        foreach (OrderNode node in _nodes)
        {
            _standing.TryGetValue(node.Id, out Held? held);
            switch (node.Kind)
            {
                case OrderKind.Enter:
                    // An event is of its candle: a computation again in the same candle sees it again, but it gives one order.
                    if (!_run.Read(node.When).Happened || held is not null || (_entered.TryGetValue(node.Id, out DateTimeOffset entered) && entered == _close))
                    {
                        break;
                    }

                    _entered[node.Id] = _close;

                    decimal? size = _run.Read(node.Size!.Value).Number;
                    if (exits.After is { } after && moment + after > _end)
                    {
                        // The cycle would end after the end of the history (spec strategy-preview «Только разведка»).
                        _orders.Add(Outcome(node.Kind, moment, node.Id, node.Side, OrderResult.Excluded, OrderExclusion.Period, null, null, null));
                    }
                    else if (_cycle is not null)
                    {
                        // Skipped whatever its plan, as the order of a strategy by plan (spec strategy-position «Одна
                        // позиция на пару»): the position is looked at before the values of the order.
                        _orders.Add(Outcome(node.Kind, moment, node.Id, node.Side, OrderResult.Skipped, null, null, null, null));
                    }
                    else if (size is not > 0 || !exits.Valid)
                    {
                        _orders.Add(Outcome(node.Kind, moment, node.Id, node.Side, OrderResult.Excluded, OrderExclusion.Plan, null, null, null));
                    }
                    else
                    {
                        Place(node, moment, activeFrom, price: null, (double)size.Value, exits);
                    }

                    break;
                case OrderKind.Market:
                    if (held is null && _run.Read(node.When).Boolean == true && _run.Read(node.Size!.Value).Number is > 0 and { } amount)
                    {
                        Place(node, moment, activeFrom, price: null, (double)amount, exits);
                    }

                    break;
                case OrderKind.Limit:
                    decimal? price = _run.Read(node.Price!.Value).Number;
                    decimal? limitSize = _run.Read(node.Size!.Value).Number;
                    if (_run.Read(node.When).Boolean == true && price is > 0 && limitSize is > 0)
                    {
                        if (held is null || held.Order.Price != (double)price.Value || held.Order.Size != (double)limitSize.Value)
                        {
                            Place(node, moment, activeFrom, (double)price.Value, (double)limitSize.Value, exits);
                        }
                    }
                    else if (held is not null)
                    {
                        _standing.Remove(node.Id);
                    }

                    break;
                default:
                    if (held is null && _cycle is { } cycle && _run.Read(node.When).Boolean == true)
                    {
                        var order = new StandingOrder { Node = node.Id, Side = cycle.IsLong ? FillSide.Sell : FillSide.Buy, Size = 0, Placed = moment };
                        _standing[node.Id] = new Held(order, OrderKind.ClosePosition, activeFrom, exits);
                    }

                    break;
            }
        }
    }

    private void Place(OrderNode node, DateTimeOffset moment, long activeFrom, double? price, double size, Exits exits)
    {
        var order = new StandingOrder { Node = node.Id, Side = node.Side, Price = price, Size = size, SizeInQuantity = node.SizeInQuantity, Placed = moment };
        _standing[node.Id] = new Held(order, node.Kind, activeFrom, exits);
    }

    // The stop, the target and the time the exit nodes give now (spec strategy-position «Недопустимая заявка»): a node that
    // is there and gives no allowed value makes an order that would open a cycle not allowed.
    private Exits ReadExits()
    {
        TimeSpan? after = _after is { } afterSource ? _run.Read(afterSource).Duration : null;
        bool afterValid = _after is null || (after is { } horizon && horizon >= TimeSpan.FromMinutes(1) && horizon.Ticks % TimeSpan.TicksPerMinute == 0 && horizon <= TimeSpan.FromDays(365));
        (bool stopValid, double? stop) = Fraction(_stop);
        (bool targetValid, double? target) = Fraction(_target);
        return new Exits(afterValid && stopValid && targetValid, stop, target, after);
    }

    // A decimal of the graph goes through its text, as in EntryPlan, so it becomes the double of the plan of a hypothesis.
    private (bool Valid, double? Value) Fraction(InputSource? source)
    {
        if (source is not { } input)
        {
            return (true, null);
        }

        if (_run.Read(input).Number is not { } value || value <= 0 || value >= 1)
        {
            return (false, null);
        }

        return (true, double.Parse(value.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private PositionState State()
    {
        if (_cycle is not { } cycle)
        {
            return PositionState.Closed;
        }

        return new PositionState((decimal)cycle.Quantity, (decimal)cycle.Invested, (decimal)cycle.AveragePrice, (decimal)cycle.LastEntryPrice, cycle.LastEntryTime);
    }

    // An order of enter is long or short; a fill of the orders of the other nodes is a buy or a sale.
    private static OrderOutcome Outcome(
        OrderKind kind,
        DateTimeOffset moment,
        string node,
        FillSide side,
        OrderResult result,
        OrderExclusion? exclusion,
        double? price,
        double? amount,
        int? cycle)
    {
        OrderSide orderSide = kind == OrderKind.Enter
            ? side == FillSide.Buy ? OrderSide.Long : OrderSide.Short
            : side == FillSide.Buy ? OrderSide.Buy : OrderSide.Sell;
        return new OrderOutcome(moment, node, orderSide, result, exclusion, price, amount, cycle);
    }

    private static double Sign(CycleRun cycle)
    {
        return cycle.IsLong ? 1 : -1;
    }

    private static InputSource? Input(CompiledGraph graph, string kind, string input)
    {
        return graph.Nodes.FirstOrDefault(node => node.Definition.Kind == kind) is { } found ? InputOf(graph, found, input) : null;
    }

    private static InputSource InputOf(CompiledGraph graph, CompiledNode node, string input)
    {
        int index = node.Kind.Inputs(graph.Graph).Select(pin => pin.Name).ToList().IndexOf(input);
        return node.Inputs[index];
    }

    private enum OrderKind
    {
        Enter,
        Market,
        Limit,
        ClosePosition
    }

    private enum CandidateKind
    {
        Stop,
        Target,
        Order
    }

    private readonly record struct Candidate(CandidateKind Kind, Held? Held, int Index);

    private sealed record Held(StandingOrder Order, OrderKind Kind, long ActiveFrom, Exits Exits);

    private sealed record Exits(bool Valid, double? Stop, double? Target, TimeSpan? After);

    // A node of an order and where its inputs come from.
    private sealed record OrderNode(string Id, OrderKind Kind, FillSide Side, bool SizeInQuantity, InputSource When, InputSource? Size, InputSource? Price)
    {
        public static OrderNode Of(CompiledGraph graph, CompiledNode node)
        {
            NodeDefinition definition = node.Definition;
            (OrderKind kind, FillSide side) = definition.Kind switch
            {
                NodeCatalog.Enter => (OrderKind.Enter, definition.Settings[NodeCatalog.Side] == NodeCatalog.Long ? FillSide.Buy : FillSide.Sell),
                NodeCatalog.MarketOrder => (OrderKind.Market, SideOf(definition)),
                NodeCatalog.LimitOrder => (OrderKind.Limit, SideOf(definition)),
                _ => (OrderKind.ClosePosition, FillSide.Sell)
            };
            bool hasSize = kind is not OrderKind.ClosePosition;
            return new OrderNode(
                definition.Id,
                kind,
                side,
                definition.Settings.TryGetValue(NodeCatalog.SizeIn, out string? unit) && unit == NodeCatalog.Quantity,
                InputOf(graph, node, NodeCatalog.When),
                hasSize ? InputOf(graph, node, NodeCatalog.Size) : null,
                kind == OrderKind.Limit ? InputOf(graph, node, NodeCatalog.Price) : null);
        }

        private static FillSide SideOf(NodeDefinition definition)
        {
            return definition.Settings[NodeCatalog.Side] == NodeCatalog.Buy ? FillSide.Buy : FillSide.Sell;
        }
    }
}
