using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     Random entries of the events of one pair (spec hypothesis-bench «Случайные входы»): for an event,
///     <c>randomsPerEvent</c> closes of candles of the timeframe in its block and period, where the pair had no event, drawn
///     with replacement. The seed comes from the hash of the hypothesis and the symbol, so a run draws the same entries every
///     time. One generator serves the pair: a run draws block by block and event by event in the order of time, and only for
///     the events the position rule did not skip — which events those are is known after the cycles of the block.
/// </summary>
public sealed class RandomEntries
{
    private readonly SplitMix64 _random;
    private readonly RunDesign _design;
    private readonly HashSet<DateTimeOffset> _taken;

    /// <summary>
    ///     <paramref name="events" /> are all the events of the pair in the period, the skipped ones included: the moment of
    ///     none of them becomes a random entry.
    /// </summary>
    public RandomEntries(string symbol, IEnumerable<DateTimeOffset> events, RunDesign design)
    {
        _random = new SplitMix64(Seed(design.Hypothesis.Hash, symbol));
        _design = design;
        _taken = [.. events];
    }

    /// <summary>
    ///     The closes of the timeframe in the block and the period where the pair had no event, in time order.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> Candidates(long block)
    {
        long frame = _design.Hypothesis.Timeframe.Ticks;
        DateTimeOffset blockStart = Blocks.Start(block, _design.Hypothesis.Block);
        DateTimeOffset start = blockStart > _design.PeriodStart ? blockStart : _design.PeriodStart;
        DateTimeOffset end = blockStart + _design.BlockLength;
        long first = (start.UtcTicks + frame - 1) / frame * frame;
        List<DateTimeOffset> candidates = [];
        for (long ticks = first; ticks < end.UtcTicks && ticks <= _design.LastEntry.UtcTicks; ticks += frame)
        {
            var moment = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (!_taken.Contains(moment))
            {
                candidates.Add(moment);
            }
        }

        return candidates;
    }

    /// <summary>
    ///     The entries of one event of a block with these candidates; none when the block has no candidate.
    /// </summary>
    public DateTimeOffset[] Draw(IReadOnlyList<DateTimeOffset> candidates)
    {
        return candidates.Count == 0
            ? []
            : [.. Enumerable.Range(0, _design.Hypothesis.RandomsPerEvent).Select(_ => candidates[_random.Below(candidates.Count)])];
    }

    private static ulong Seed(string hypothesisHash, string symbol)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{hypothesisHash}:{symbol}"));
        return BitConverter.ToUInt64(digest, 0);
    }
}
