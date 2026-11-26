using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The orders of a set over every pair of a backtest, as the first pass found them (change fix-backtest-random-control,
///     design.md, decision 4): the orders in history, the ones the position will skip included. A pair takes the random
///     entries of a week from the orders of the other pairs of that week, and none at a moment where it has an order of its
///     own.
/// </summary>
public sealed class SetOrders
{
    private readonly Dictionary<long, (PairOrder Order, string[] Symbols)[]> _weeks;
    private readonly HashSet<(string Symbol, DateTimeOffset Moment)> _moments;

    /// <summary>
    ///     The weeks with at least one order, in time order.
    /// </summary>
    public IReadOnlyList<long> Weeks { get; }

    /// <param name="pairs">The orders of each pair.</param>
    /// <param name="block">What a week is: a test may use days.</param>
    public SetOrders(IReadOnlyDictionary<string, IReadOnlyList<PairOrder>> pairs, TimeBlock block)
    {
        _moments = [.. pairs.SelectMany(pair => pair.Value.Select(order => (pair.Key, order.Moment)))];
        _weeks = pairs
            .SelectMany(pair => pair.Value.Select(order => (Order: order, Symbol: pair.Key)))
            .GroupBy(item => Blocks.Of(item.Order.Moment, block))
            .ToDictionary(
                week => week.Key,
                week => week
                    .GroupBy(item => item.Order)
                    .OrderBy(same => same.Key.Moment)
                    .ThenBy(same => same.Key.Side)
                    .Select(same => (same.Key, same.Select(item => item.Symbol).Distinct(StringComparer.Ordinal).ToArray()))
                    .ToArray());
        Weeks = [.. _weeks.Keys.Order()];
    }

    /// <summary>
    ///     The moments and sides of the orders of the pairs other than <paramref name="symbol" /> in <paramref name="week" />,
    ///     each once, in the order of time and then of side.
    /// </summary>
    public IReadOnlyList<PairOrder> Others(long week, string symbol)
    {
        return _weeks.TryGetValue(week, out (PairOrder Order, string[] Symbols)[]? orders)
            ? [.. orders.Where(order => order.Symbols.Any(other => !string.Equals(other, symbol, StringComparison.Ordinal))).Select(order => order.Order)]
            : [];
    }

    /// <summary>
    ///     Whether <paramref name="symbol" /> has an order at <paramref name="moment" />, on either side.
    /// </summary>
    public bool Has(string symbol, DateTimeOffset moment)
    {
        return _moments.Contains((symbol, moment));
    }
}