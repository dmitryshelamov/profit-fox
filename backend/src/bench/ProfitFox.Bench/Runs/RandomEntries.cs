using System.Security.Cryptography;
using System.Text;

namespace ProfitFox.Bench.Runs;

/// <summary>
///     Random entries of the events of one pair (spec hypothesis-bench): for each event, <c>randomsPerEvent</c> closes of
///     candles of the timeframe in its block and period, where the pair had no event, drawn with replacement. The seed comes
///     from the hash of the hypothesis and the symbol, so a run draws the same entries every time.
/// </summary>
public static class RandomEntries
{
    /// <summary>
    ///     The entries of each event, in the order of the events; an event whose block has no candidate gets none.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset[]> Draw(string symbol, IReadOnlyList<DateTimeOffset> events, RunDesign design)
    {
        var random = new SplitMix64(Seed(design.Hypothesis.Hash, symbol));
        long frame = design.Hypothesis.Timeframe.Ticks;
        var taken = new HashSet<DateTimeOffset>(events);
        var entries = new DateTimeOffset[events.Count][];
        foreach (IGrouping<long, int> block in Enumerable.Range(0, events.Count).GroupBy(index => Blocks.Of(events[index], design.Hypothesis.Block)).OrderBy(group => group.Key))
        {
            DateTimeOffset blockStart = Blocks.Start(block.Key, design.Hypothesis.Block);
            DateTimeOffset start = blockStart > design.PeriodStart ? blockStart : design.PeriodStart;
            DateTimeOffset end = blockStart + design.BlockLength;
            long first = (start.UtcTicks + frame - 1) / frame * frame;
            List<DateTimeOffset> candidates = [];
            for (long ticks = first; ticks < end.UtcTicks && ticks <= design.LastEntry.UtcTicks; ticks += frame)
            {
                var moment = new DateTimeOffset(ticks, TimeSpan.Zero);
                if (!taken.Contains(moment))
                {
                    candidates.Add(moment);
                }
            }

            foreach (int index in block.OrderBy(index => events[index]))
            {
                entries[index] = candidates.Count == 0
                    ? []
                    : [.. Enumerable.Range(0, design.Hypothesis.RandomsPerEvent).Select(_ => candidates[random.Below(candidates.Count)])];
            }
        }

        return entries;
    }

    private static ulong Seed(string hypothesisHash, string symbol)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{hypothesisHash}:{symbol}"));
        return BitConverter.ToUInt64(digest, 0);
    }
}
