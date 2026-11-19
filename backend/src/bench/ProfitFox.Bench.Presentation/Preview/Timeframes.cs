namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     The timeframes of a signal, as the hypothesis files write them.
/// </summary>
public static class Timeframes
{
    private static readonly (string Name, TimeSpan Length)[] Known =
        [("1h", TimeSpan.FromHours(1)), ("4h", TimeSpan.FromHours(4)), ("1d", TimeSpan.FromDays(1))];

    public static bool TryParse(string? name, out TimeSpan length)
    {
        foreach ((string known, TimeSpan value) in Known)
        {
            if (known == name)
            {
                length = value;
                return true;
            }
        }

        length = default;
        return false;
    }

    public static string Name(TimeSpan length)
    {
        foreach ((string name, TimeSpan value) in Known)
        {
            if (value == length)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(length), length, "not a timeframe of a signal");
    }
}
