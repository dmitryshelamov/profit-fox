using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     Random entries of the orders of one pair (spec strategy-backtest «Случайные входы»): for an order,
///     <see cref="BacktestDesign.Randoms" /> closes of candles of the timeframe in its block and in history, where the pair
///     had no order, drawn with replacement. A close is in history when its trade with the time of its candle is
///     (<see cref="BacktestDesign.Fits" />). The seed comes from the seed of the design and the symbol, so a run draws the
///     same entries every time. One generator serves the pair: a run draws block by block and order by order in the order
///     of time, and only for the orders the position rule did not skip — which orders those are is known after the cycles
///     of the block.
/// </summary>
public sealed class RandomEntries
{
    private readonly SplitMix64 _random;
    private readonly BacktestDesign _design;
    private readonly Func<DateTimeOffset, TimeSpan?> _after;
    private readonly HashSet<DateTimeOffset> _taken;

    /// <summary>
    ///     <paramref name="events" /> are all the orders of the pair in history, the skipped ones included: the moment of
    ///     none of them becomes a random entry. <paramref name="after" /> gives the time of the candle at a moment.
    /// </summary>
    public RandomEntries(string symbol, IEnumerable<DateTimeOffset> events, BacktestDesign design, Func<DateTimeOffset, TimeSpan?> after)
    {
        _random = new SplitMix64(Seed(design.Seed, symbol));
        _design = design;
        _after = after;
        _taken = [.. events];
    }


    /// <summary>
    ///     The closes of the timeframe in the block and in history where the pair had no order, in time order.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> Candidates(long block)
    {
        long frame = _design.Timeframe.Ticks;
        DateTimeOffset blockStart = Blocks.Start(block, _design.Block);
        DateTimeOffset start = blockStart > _design.Start ? blockStart : _design.Start;
        DateTimeOffset end = blockStart + _design.BlockLength;
        long first = (start.UtcTicks + frame - 1) / frame * frame;
        List<DateTimeOffset> candidates = [];
        for (long ticks = first; ticks < end.UtcTicks && ticks <= _design.End.UtcTicks; ticks += frame)
        {
            var moment = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (!_taken.Contains(moment) && _design.Fits(moment, _after(moment)))
            {
                candidates.Add(moment);
            }
        }

        return candidates;
    }

    /// <summary>
    ///     The entries of one order of a block with these candidates; none when the block has no candidate.
    /// </summary>
    public DateTimeOffset[] Draw(IReadOnlyList<DateTimeOffset> candidates)
    {
        return candidates.Count == 0
            ? []
            : [.. Enumerable.Range(0, _design.Randoms).Select(_ => candidates[_random.Below(candidates.Count)])];
    }

    private static ulong Seed(string seed, string symbol)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{symbol}"));
        return BitConverter.ToUInt64(digest, 0);
    }
}
