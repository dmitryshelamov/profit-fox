using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Positions;

/// <summary>
///     One cycle of a position on the minute path of a pair (change add-position-node, design.md, decisions 5 and 8): its
///     fills, the position they leave, and its result. This is the one calculation of a cycle: the bench, a cycle opened
///     outside the graph and the preview count with it, a strategy by plan and a strategy with orders alike. The rules of a
///     trade of a plan (spec trade-plan-outcome) are written here once: <see cref="Simulate" /> walks a plan of one entry,
///     and the run of a strategy with orders uses the same minute rules (<see cref="StopHit" />, <see cref="TargetHit" />,
///     <see cref="StopExit" />) and the same accounting.
///     <para>
///         The fraction of a cycle is counted from prices, each entry weighted by its amount over the largest money put
///         in, and a decrease closes the same share of every entry. A cycle of one entry has the weight 1, and its numbers
///         are the expressions of a trade of a plan: the side times <c>exit / entry − 1</c> before costs and the fee rate
///         times <c>1 + exit / entry</c>. With them the bench keeps the numbers of the Python bench (<c>outcome.py</c>)
///         and the digests of its engine version.
///     </para>
/// </summary>
public sealed class CycleRun
{
    // A share left this close to the whole is the whole: the size of a sale read back from the graph is a decimal.
    private const double Whole = 1 - 1e-12;

    private readonly MinutePath _path;
    private readonly TradeCosts _costs;
    private readonly double _side;
    private readonly List<Lot> _lots = [];
    private readonly List<Portion> _portions = [];
    private readonly List<CycleFill> _fills = [];
    private CycleExit _reason = CycleExit.None;

    public EntrySide Side { get; }

    /// <summary>
    ///     The stop and the target as fractions of the average price, and the moment the cycle closes by its time: those of
    ///     the computation the order that opened the cycle came from. Null when the strategy has no such node.
    /// </summary>
    public double? Stop { get; private init; }

    public double? Target { get; private init; }

    public DateTimeOffset? Until { get; private init; }

    /// <summary>
    ///     Whether the order of fills in one of the minutes of the cycle decided its course.
    /// </summary>
    public bool Ambiguous { get; set; }

    public double MaxInvested { get; private set; }

    public bool Closed { get; private set; }

    public int Entries => _lots.Count;

    public double Quantity => Closed ? 0 : Sum(_lots.Select(lot => lot.Quantity * lot.Remaining));

    public double Invested => Closed ? 0 : Sum(_lots.Select(lot => lot.Amount * lot.Remaining));

    /// <summary>
    ///     The money put in over the quantity; a cycle of one entry has the price of its fill, which a decrease keeps.
    /// </summary>
    public double AveragePrice => _lots.Count == 1 ? _lots[0].Fill : Invested / Quantity;

    public double LastEntryPrice => _lots[^1].Fill;

    public DateTimeOffset LastEntryTime => _lots[^1].Time;

    /// <summary>
    ///     The price of the stop and of the target now: from the average price, so they move after an increase.
    /// </summary>
    public double StopLevel => Stop is { } stop ? AveragePrice * (1 - _side * stop) : double.NaN;

    public double TargetLevel => Target is { } target ? AveragePrice * (1 + _side * target) : double.NaN;

    public bool IsLong => Side == EntrySide.Long;

    public IReadOnlyList<CycleFill> Fills => _fills;

    private CycleRun(MinutePath path, TradeCosts costs, EntrySide side)
    {
        _path = path;
        _costs = costs;
        Side = side;
        _side = side == EntrySide.Long ? 1 : -1;
    }

    /// <summary>
    ///     A cycle opened by a fill of <paramref name="amount" /> USDT at <paramref name="fill" />; <paramref name="raw" /> is
    ///     the price of the minute before the slippage.
    /// </summary>
    public static CycleRun Open(
        MinutePath path,
        TradeCosts costs,
        EntrySide side,
        DateTimeOffset moment,
        double raw,
        double fill,
        double amount,
        string? node,
        double? stop = null,
        double? target = null,
        DateTimeOffset? until = null,
        bool maker = false)
    {
        var cycle = new CycleRun(path, costs, side) { Stop = stop, Target = target, Until = until };
        cycle.Add(moment, raw, fill, amount, node, FillAction.Entry, maker);
        return cycle;
    }

    /// <summary>
    ///     A fill of the side of the position: the quantity grows by the amount over the price, the money put in by the
    ///     amount (spec strategy-position «Увеличение позиции»). A fill of a limit order is <paramref name="maker" />.
    /// </summary>
    public void Increase(DateTimeOffset moment, double raw, double fill, double amount, string? node, bool maker = false)
    {
        if (Closed)
        {
            throw new InvalidOperationException("A closed cycle takes no fill.");
        }

        Add(moment, raw, fill, amount, node, FillAction.Increase, maker);
    }

    /// <summary>
    ///     A fill of the other side: at most the quantity of the position, the same share of every entry, the average price
    ///     kept (spec strategy-position «Уменьшение позиции»). Gives the quantity sold; the cycle closes when nothing is left,
    ///     and <paramref name="reason" /> is then how it closed.
    /// </summary>
    public double Reduce(DateTimeOffset moment, double raw, double fill, double quantity, CycleExit reason, string? node, bool maker = false)
    {
        if (Closed)
        {
            throw new InvalidOperationException("A closed cycle takes no fill.");
        }

        double held = Quantity;
        double share = quantity / held;
        bool whole = share >= Whole;
        double sold = whole ? held : quantity;
        for (int index = 0; index < _lots.Count; index++)
        {
            Lot lot = _lots[index];
            double part = whole ? lot.Remaining : lot.Remaining * share;
            _portions.Add(new Portion(index, part, raw, fill, moment, Estimate: false, maker));
            _lots[index] = lot with { Remaining = whole ? 0 : lot.Remaining - part };
        }

        Closed = whole;
        if (whole)
        {
            _reason = reason;
        }

        _fills.Add(new CycleFill(moment, whole ? FillAction.Exit : FillAction.Decrease, IsLong ? FillSide.Sell : FillSide.Buy, fill, sold, sold * fill, node));
        return sold;
    }

    /// <summary>
    ///     The whole position closed by one fill.
    /// </summary>
    public void Close(DateTimeOffset moment, double raw, double fill, CycleExit reason, string? node = null)
    {
        Reduce(moment, raw, fill, Quantity, reason, node);
    }

    /// <summary>
    ///     The result of a closed cycle.
    /// </summary>
    public CycleOutcome Outcome()
    {
        if (!Closed)
        {
            throw new InvalidOperationException("The cycle is still open: its result is an estimate.");
        }

        return Result(_portions, open: false);
    }

    /// <summary>
    ///     The estimate of a cycle open at the end of the computed history (spec strategy-position «Незакрытый цикл и дыра в
    ///     истории»): what is left valued at <paramref name="lastClose" />, the close of the last minute with trades, at
    ///     <paramref name="end" />, without the fee of an exit.
    /// </summary>
    public CycleOutcome Estimate(DateTimeOffset end, double lastClose)
    {
        if (Closed)
        {
            return Outcome();
        }

        List<Portion> portions = [.. _portions];
        portions.AddRange(_lots.Select((lot, index) => new Portion(index, lot.Remaining, lastClose, lastClose, end, Estimate: true, Maker: false)).Where(portion => portion.Share > 0));
        return Result(portions, open: true);
    }

    /// <summary>
    ///     The cycle of a plan of one entry of <paramref name="amount" /> USDT from <paramref name="moment" /> (spec
    ///     trade-plan-outcome), by the rules of the Python bench (<c>outcome.py</c>), so the two give the same numbers:
    ///     <list type="bullet">
    ///         <item>entry at the open of the first minute with trades from the moment of the event, worse by the slippage;</item>
    ///         <item>the path runs from the entry minute to the end of the horizon, until the stop or the target;</item>
    ///         <item>stop and target in one minute: the stop counts, the cycle is ambiguous;</item>
    ///         <item>the stop fills at the worse of its price and the open of the minute, with slippage;</item>
    ///         <item>the target fills at its price without slippage, also on a gap through it;</item>
    ///         <item>otherwise the cycle closes at the end of the horizon at the close of its last minute with trades;</item>
    ///         <item>the fee is taken on both sides, funding on the payments at <c>entry &lt; t ≤ exit</c>;</item>
    ///         <item>coverage of candles and of funding is judged on the whole horizon, not up to the exit.</item>
    ///     </list>
    ///     <paramref name="node" /> is the node of the order that opened the cycle; null for a cycle opened outside the graph.
    /// </summary>
    public static CycleOutcome Simulate(MinutePath path, DateTimeOffset moment, TradePlan plan, TradeCosts costs, double amount, string? node = null)
    {
        EntrySide side = plan.Side == TradeSide.Long ? EntrySide.Long : EntrySide.Short;
        (TradeStatus status, int start, int entryStep, int lastTraded) = Prepare(path, moment, plan, costs);
        if (status != TradeStatus.Ok)
        {
            return CycleOutcome.Excluded(status, side);
        }

        CycleRun cycle = Enter(path, moment, plan, costs, amount, node, start, entryStep);
        (int hitStep, bool stopHit, bool targetHit) = cycle.Walk(start, entryStep, plan.HorizonMinutes);
        CycleExit reason = stopHit ? CycleExit.Stop : targetHit ? CycleExit.Target : CycleExit.Time;
        double exitRaw = reason switch
        {
            CycleExit.Stop => StopExit(cycle.IsLong, cycle.StopLevel, path.Open(start + hitStep)),
            CycleExit.Target => cycle.TargetLevel,
            _ => path.Close(start + lastTraded)
        };
        double exitSlippage = reason == CycleExit.Target ? 0 : costs.Slippage;
        double exitFill = exitRaw * (1 - cycle._side * exitSlippage);
        int exitStep = reason == CycleExit.Time ? plan.HorizonMinutes : hitStep;
        cycle.Ambiguous = stopHit && targetHit;
        cycle.Close(moment + TimeSpan.FromMinutes(exitStep), exitRaw, exitFill, reason);
        return cycle.Outcome();
    }

    /// <summary>
    ///     The moment the cycle of a plan ends, without its result: the path is walked to the stop, the target or the end of
    ///     the horizon as in <see cref="Simulate" />, and nothing is computed of what the cycle earned. This is what the bench
    ///     may know of an event before it is allowed to read its outcome, and what a position needs to know when the pair is
    ///     free again (spec strategy-position).
    /// </summary>
    public static TradeExit Exit(MinutePath path, DateTimeOffset moment, TradePlan plan, TradeCosts costs)
    {
        (TradeStatus status, int start, int entryStep, _) = Prepare(path, moment, plan, costs);
        if (status != TradeStatus.Ok)
        {
            return new TradeExit(status, Time: null);
        }

        CycleRun cycle = Enter(path, moment, plan, costs, amount: 1, node: null, start, entryStep);
        (int hitStep, _, _) = cycle.Walk(start, entryStep, plan.HorizonMinutes);
        return new TradeExit(TradeStatus.Ok, moment + TimeSpan.FromMinutes(hitStep >= 0 ? hitStep : plan.HorizonMinutes));
    }

    /// <summary>
    ///     The price of a market fill: the open of the minute, worse by the slippage for the side of the fill.
    /// </summary>
    public static double MarketFill(double open, FillSide side, TradeCosts costs)
    {
        double direction = side == FillSide.Buy ? 1 : -1;
        return open * (1 + direction * costs.Slippage);
    }

    /// <summary>
    ///     Whether the stop of a long or a short at <paramref name="level" /> is reached in a minute.
    /// </summary>
    public static bool StopHit(bool isLong, double high, double low, double level)
    {
        return isLong ? low <= level : high >= level;
    }

    public static bool TargetHit(bool isLong, double high, double low, double level)
    {
        return isLong ? high >= level : low <= level;
    }

    /// <summary>
    ///     The price a stop leaves at before the slippage: the worse of its level and the open of the minute.
    /// </summary>
    public static double StopExit(bool isLong, double level, double open)
    {
        return double.IsNaN(open) ? level : isLong ? Math.Min(level, open) : Math.Max(level, open);
    }

    private static CycleRun Enter(MinutePath path, DateTimeOffset moment, TradePlan plan, TradeCosts costs, double amount, string? node, int start, int entryStep)
    {
        EntrySide side = plan.Side == TradeSide.Long ? EntrySide.Long : EntrySide.Short;
        double entryRaw = path.Open(start + entryStep);
        double entryFill = MarketFill(entryRaw, side == EntrySide.Long ? FillSide.Buy : FillSide.Sell, costs);
        return Open(
            path,
            costs,
            side,
            moment + TimeSpan.FromMinutes(entryStep),
            entryRaw,
            entryFill,
            amount,
            node,
            plan.Stop,
            plan.Target,
            moment + plan.Horizon);
    }

    // The path from the entry minute to the end of the horizon, until the stop or the target.
    private (int HitStep, bool StopHit, bool TargetHit) Walk(int start, int entryStep, int horizon)
    {
        double stopLevel = StopLevel;
        double targetLevel = TargetLevel;
        for (int step = entryStep; step < horizon; step++)
        {
            double high = _path.High(start + step);
            double low = _path.Low(start + step);
            bool stopHit = Stop is not null && StopHit(IsLong, high, low, stopLevel);
            bool targetHit = Target is not null && TargetHit(IsLong, high, low, targetLevel);
            if (stopHit || targetHit)
            {
                return (step, stopHit, targetHit);
            }
        }

        return (-1, false, false);
    }

    // The checks of coverage, trades and funding on the whole horizon, and where the entry and the last trade are.
    private static (TradeStatus Status, int Start, int EntryStep, int LastTraded) Prepare(MinutePath path, DateTimeOffset moment, TradePlan plan, TradeCosts costs)
    {
        if (moment.UtcTicks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException("An entry moment is on a minute.", nameof(moment));
        }

        int horizon = plan.HorizonMinutes;
        long first = path.MinuteIndex(moment);
        if (first < 0 || first + horizon > path.Minutes)
        {
            return (TradeStatus.Coverage, 0, -1, -1);
        }

        int start = (int)first;
        int entryStep = -1;
        int lastTraded = -1;
        for (int step = 0; step < horizon; step++)
        {
            if (!path.Covered(start + step))
            {
                return (TradeStatus.Coverage, start, -1, -1);
            }

            if (!double.IsNaN(path.Open(start + step)))
            {
                entryStep = entryStep < 0 ? step : entryStep;
                lastTraded = step;
            }
        }

        if (entryStep < 0)
        {
            return (TradeStatus.NoTrade, start, -1, -1);
        }

        if (costs.Funding && path.FundingFrom is { } fundingFrom && !(moment >= fundingFrom && moment + plan.Horizon < path.FundingUntil))
        {
            return (TradeStatus.Funding, start, -1, -1);
        }

        return (TradeStatus.Ok, start, entryStep, lastTraded);
    }

    private void Add(DateTimeOffset moment, double raw, double fill, double amount, string? node, FillAction action, bool maker)
    {
        double quantity = amount / fill;
        _lots.Add(new Lot(moment, raw, fill, amount, quantity, Remaining: 1, maker));
        MaxInvested = Math.Max(MaxInvested, Invested);
        _fills.Add(new CycleFill(moment, action, IsLong ? FillSide.Buy : FillSide.Sell, fill, quantity, amount, node));
    }

    // The fractions of the largest money put in, summed over the shares of the entries each exit closed.
    private CycleOutcome Result(List<Portion> portions, bool open)
    {
        double largest = MaxInvested;
        double Weight(Portion portion)
        {
            return _lots[portion.Lot].Amount / largest * portion.Share;
        }

        double gross = Sum(portions.Select(portion => Weight(portion) * (_side * (portion.Raw / _lots[portion.Lot].Raw - 1))));
        double traded = Sum(portions.Select(portion => Weight(portion) * (_side * (portion.Fill / _lots[portion.Lot].Fill - 1))));
        // The turnover of the fills as fractions, the taker ones and the maker ones; a cycle of taker fills only keeps the
        // expression of a trade of a plan.
        double Turnover(bool maker)
        {
            return Sum(_lots.Where(lot => lot.Maker == maker).Select(lot => lot.Amount / largest).Concat(portions.Where(portion => !portion.Estimate && portion.Maker == maker)
                .Select(portion => Weight(portion) * (portion.Fill / _lots[portion.Lot].Fill))));
        }

        bool anyMaker = _lots.Any(lot => lot.Maker) || portions.Any(portion => !portion.Estimate && portion.Maker);
        double fees = anyMaker ? _costs.TakerFee * Turnover(maker: false) + _costs.MakerFee * Turnover(maker: true) : _costs.TakerFee * Turnover(maker: false);
        double slippage = gross - traded;
        double funding = _costs.Funding && _path.FundingFrom is not null
            ? Sum(portions.Select(portion => Weight(portion) * (-_side * _path.FundingPaid(_lots[portion.Lot].Time, portion.Time) / _lots[portion.Lot].Fill)))
            : 0;
        double net = traded - fees + funding;
        double? r = Stop is { } stop && _lots.Count == 1 ? net / stop : null;
        return new CycleOutcome(
            TradeStatus.Ok,
            Side,
            [.. _fills],
            _lots.Count,
            largest,
            open ? CycleExit.None : _reason,
            Ambiguous,
            gross,
            fees,
            slippage,
            funding,
            net,
            net * largest,
            r,
            open);
    }

    // A sum that starts from its first term, so that one term of -0 stays -0 as the expression of one entry gives it.
    private static double Sum(IEnumerable<double> terms)
    {
        double? sum = null;
        foreach (double term in terms)
        {
            sum = sum is { } value ? value + term : term;
        }

        return sum ?? 0;
    }

    // An entry: when and at what price it filled, its amount and quantity, and the share of it still held.
    private sealed record Lot(DateTimeOffset Time, double Raw, double Fill, double Amount, double Quantity, double Remaining, bool Maker);

    // The share of an entry an exit closed, at what prices and when, and whether it paid the maker fee; an estimate values an
    // open cycle and pays no fee.
    private sealed record Portion(int Lot, double Share, double Raw, double Fill, DateTimeOffset Time, bool Estimate, bool Maker);
}
