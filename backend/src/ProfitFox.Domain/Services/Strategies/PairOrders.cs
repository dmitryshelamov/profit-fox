using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Hypotheses.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Strategies;

/// <summary>
///     The entry orders of a strategy on one pair and what became of each (spec strategy-preview; change
///     add-strategy-constructor, design.md, decision 6). The orders are split by weeks (UTC, from Monday, as the block
///     <c>week</c> of the bench); trades are computed for at most <see cref="MaxWeeks" /> last weeks with orders, on the
///     minute path of each week, through one position of the pair for the whole computed part, so a position opened in one
///     week holds the pair in the next. An order whose trade would end after <c>end</c> is not simulated. Weeks go in the
///     order of time.
/// </summary>
public sealed class PairOrders
{
    /// <summary>
    ///     The most weeks with orders whose trades are computed (spec strategy-preview «Предел минутной истории»): 8, as the
    ///     owner chose after the measure of task 5.4 showed a first preview of 26 weeks taking up to 52 seconds.
    /// </summary>
    public const int MaxWeeks = 8;

    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly StrategyStep[] _orders;
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;
    private readonly TradeCosts _costs;
    private readonly ILookup<long, StrategyStep> _simulated;
    private readonly HashSet<long> _computed;
    private readonly PairPosition _position = new PairPosition();
    private readonly Dictionary<DateTimeOffset, OrderOutcome> _outcomes = [];
    private long? _lastWeek;

    /// <summary>
    ///     The weeks whose trades are computed, in time order.
    /// </summary>
    public IReadOnlyList<long> Weeks { get; }

    /// <summary>
    ///     The start of the first computed week when earlier weeks with orders are not computed; null when every week is.
    /// </summary>
    public DateTimeOffset? ComputedFrom { get; }

    /// <summary>
    ///     The orders of <paramref name="steps" /> over [<paramref name="start" />, <paramref name="end" />].
    /// </summary>
    public PairOrders(PairSteps steps, DateTimeOffset start, DateTimeOffset end, TradeCosts costs, int maxWeeks = MaxWeeks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWeeks, 1);
        _start = start;
        _end = end;
        _costs = costs;
        _orders = [.. steps.Orders.Where(order => order.Moment >= start && order.Moment <= end)];
        _simulated = _orders.Where(order => !PastEnd(order)).ToLookup(order => Blocks.Of(order.Moment, TimeBlock.Week));
        long[] weeks = [.. _simulated.Select(week => week.Key).Order()];
        Weeks = [.. weeks.Skip(Math.Max(0, weeks.Length - maxWeeks))];
        _computed = [.. Weeks];
        ComputedFrom = weeks.Length > maxWeeks ? Blocks.Start(Weeks[0], TimeBlock.Week) : null;
    }

    /// <summary>
    ///     The minutes the trades of a week can touch: from the start of the week (not before <c>start</c>) to its end plus the
    ///     longest time of its orders, never past <c>end</c>.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Span(long week)
    {
        DateTimeOffset weekStart = Blocks.Start(week, TimeBlock.Week);
        TimeSpan longest = _simulated[week].Max(order => order.After ?? TimeSpan.Zero);
        DateTimeOffset from = weekStart > _start ? weekStart : _start;
        DateTimeOffset to = weekStart + Week + longest;
        return (from, to < _end ? to : _end);
    }

    /// <summary>
    ///     The orders of a computed week on its minute path, through the position of the pair.
    /// </summary>
    public void Evaluate(long week, MinutePath path)
    {
        if (!_computed.Contains(week))
        {
            throw new ArgumentException($"The trades of week {week} are not computed.", nameof(week));
        }

        if (week <= _lastWeek)
        {
            throw new InvalidOperationException($"Weeks go in the order of time: {week} is not after {_lastWeek}.");
        }

        _lastWeek = week;
        foreach (StrategyStep order in _simulated[week].OrderBy(order => order.Moment))
        {
            EntrySide side = order.Entry!.Value;
            _outcomes[order.Moment] = _position.Enter(path, order, _costs) switch
            {
                null => new OrderOutcome(order.Moment, side, OrderResult.Skipped, Exclusion: null, Trade: null),
                { Opened: true } cycle => new OrderOutcome(order.Moment, side, OrderResult.Opened, Exclusion: null, cycle.Outcome),
                Cycle cycle => new OrderOutcome(order.Moment, side, OrderResult.Excluded, Exclusion(cycle.Status), Trade: null)
            };
        }
    }

    /// <summary>
    ///     Every order in time order with its outcome, once every computed week is evaluated.
    /// </summary>
    public IReadOnlyList<OrderOutcome> Outcomes()
    {
        if (Weeks.Count > 0 && _lastWeek != Weeks[^1])
        {
            throw new InvalidOperationException("Every computed week is evaluated before the outcomes are read.");
        }

        return [.. _orders.Select(Outcome)];
    }

    private OrderOutcome Outcome(StrategyStep order)
    {
        if (_outcomes.TryGetValue(order.Moment, out OrderOutcome? outcome))
        {
            return outcome;
        }

        EntrySide side = order.Entry!.Value;
        return PastEnd(order)
            ? new OrderOutcome(order.Moment, side, OrderResult.Excluded, OrderExclusion.Period, Trade: null)
            : new OrderOutcome(order.Moment, side, OrderResult.NotComputed, Exclusion: null, Trade: null);
    }

    // The trade of the order would end after the end of what may be read (spec strategy-preview «Только разведка»).
    private bool PastEnd(StrategyStep order)
    {
        return order.After is { } after && order.Moment + after > _end;
    }

    private static OrderExclusion Exclusion(TradeStatus status)
    {
        return status switch
        {
            TradeStatus.Plan => OrderExclusion.Plan,
            TradeStatus.Coverage => OrderExclusion.Coverage,
            TradeStatus.NoTrade => OrderExclusion.NoTrade,
            TradeStatus.Funding => OrderExclusion.Funding,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A trade left out has a reason.")
        };
    }
}
