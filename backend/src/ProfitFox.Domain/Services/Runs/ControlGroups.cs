using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Domain.Services.Runs;

/// <summary>
///     The control groups of the weeks of a backtest on one timeframe (spec strategy-backtest «Контрольная группа недели»;
///     change fix-backtest-random-control, design.md, decisions 2 and 3): the pairs whose first candle of the timeframe
///     closed by the start of the week, the first <c>size</c> of them in the order of the SHA-256 of
///     <c>&lt;start of the week, unix ms&gt;:&lt;symbol&gt;</c>. A group depends only on the week, the pairs of the backtest
///     and their history: not on the sets, their graphs or the order the pairs are given in. One group serves every set
///     of the timeframe.
/// </summary>
public sealed class ControlGroups
{
    private readonly (string Symbol, DateTimeOffset FirstClose)[] _pairs;
    private readonly TimeBlock _block;
    private readonly int _size;
    private readonly Dictionary<long, (string[] Ordered, HashSet<string> Members)> _weeks = [];

    /// <param name="firstCloses">The close of the first candle of the timeframe of each pair that has one.</param>
    /// <param name="block">What a week is: a test may use days.</param>
    /// <param name="size">The most pairs a group has.</param>
    public ControlGroups(IReadOnlyDictionary<string, DateTimeOffset> firstCloses, TimeBlock block, int size)
    {
        _pairs = [.. firstCloses.Select(pair => (pair.Key, pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal)];
        _block = block;
        _size = size;
    }

    /// <summary>
    ///     The pairs of the group of <paramref name="week" />, in the order of the draw.
    /// </summary>
    public IReadOnlyList<string> Of(long week)
    {
        return Week(week).Ordered;
    }

    /// <summary>
    ///     Whether <paramref name="symbol" /> is in the group of <paramref name="week" />.
    /// </summary>
    public bool Contains(long week, string symbol)
    {
        return Week(week).Members.Contains(symbol);
    }

    private (string[] Ordered, HashSet<string> Members) Week(long week)
    {
        if (_weeks.TryGetValue(week, out (string[] Ordered, HashSet<string> Members) group))
        {
            return group;
        }

        DateTimeOffset start = Blocks.Start(week, _block);
        string prefix = $"{start.ToUnixTimeMilliseconds()}:";
        string[] ordered =
        [
            .. _pairs
                .Where(pair => pair.FirstClose <= start)
                .Select(pair => (pair.Symbol, Draw: SHA256.HashData(Encoding.UTF8.GetBytes(prefix + pair.Symbol))))
                .OrderBy(pair => pair.Draw, ByteOrder.Instance)
                .ThenBy(pair => pair.Symbol, StringComparer.Ordinal)
                .Take(_size)
                .Select(pair => pair.Symbol)
        ];
        group = (ordered, [.. ordered]);
        _weeks[week] = group;
        return group;
    }

    // Digests by their bytes, as unsigned numbers from the first byte.
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new ByteOrder();

        public int Compare(byte[]? x, byte[]? y)
        {
            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}