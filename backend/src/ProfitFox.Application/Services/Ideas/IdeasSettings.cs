using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     The <c>Ideas</c> section of the settings of the Worker (change add-ideas-and-market, design.md, decision 7): how many
///     pairs of one side on one candle of a timeframe make a burst — the thresholds of v2 by default.
/// </summary>
public sealed class IdeasSettings
{
    /// <summary>
    ///     The text of the built-in label.
    /// </summary>
    public const string BurstLabel = "⚡ всплеск";

    public Dictionary<string, int> Burst { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["5m"] = 60,
        ["15m"] = 25,
        ["1h"] = 8,
        ["4h"] = 4,
        ["1d"] = 4
    };

    /// <summary>
    ///     The threshold of <paramref name="timeframe" />; a timeframe without one never makes a burst.
    /// </summary>
    public int BurstOf(TimeSpan timeframe)
    {
        return Burst.TryGetValue(Timeframes.Name(timeframe), out int threshold) ? threshold : int.MaxValue;
    }
}
