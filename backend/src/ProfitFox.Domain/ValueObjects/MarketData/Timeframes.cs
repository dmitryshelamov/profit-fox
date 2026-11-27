namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The timeframes of the candles of a graph, as the requests write them: 5m, 15m, 1h, 4h and 1d (change
///     add-ideas-and-market, design.md, decision 12).
/// </summary>
public static class Timeframes
{
    private static readonly (string Name, TimeSpan Length)[] Known =
    [
        ("5m", TimeSpan.FromMinutes(5)), ("15m", TimeSpan.FromMinutes(15)), ("1h", TimeSpan.FromHours(1)), ("4h", TimeSpan.FromHours(4)),
        ("1d", TimeSpan.FromDays(1))
    ];

    /// <summary>
    ///     The names of the timeframes from the shortest.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [.. Known.Select(known => known.Name)];

    /// <summary>
    ///     The names as a refusal lists them: «5m, 15m, 1h, 4h, 1d».
    /// </summary>
    public static string Listed { get; } = string.Join(", ", All);

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

        throw new ArgumentOutOfRangeException(nameof(length), length, "not a timeframe of a graph");
    }
}
