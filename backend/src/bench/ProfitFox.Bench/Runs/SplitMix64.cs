namespace ProfitFox.Bench.Runs;

/// <summary>
///     A small deterministic random generator (Steele, Lea and Flood 2014): the same seed gives the same draws everywhere.
/// </summary>
public sealed class SplitMix64(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    ///     A whole number in [0, <paramref name="count" />), every one equally likely (rejection of the uneven tail).
    /// </summary>
    public int Below(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "There must be something to choose from.");
        }

        ulong limit = ulong.MaxValue - ulong.MaxValue % (ulong)count;
        ulong value;
        do
        {
            value = Next();
        }
        while (value >= limit);

        return (int)(value % (ulong)count);
    }
}
