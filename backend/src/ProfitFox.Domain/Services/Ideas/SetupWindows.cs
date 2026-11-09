using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The windows the setups of an interval are read over on each minute (decision 1 of speed-up-ideas-replay): the last candle, the
///     interval's length of minutes, and the impulse, <paramref name="candles" /> such lengths; both grow by every closed minute of the pair.
/// </summary>
public sealed class SetupWindows(int candleMinutes, int candles)
{
    public MinuteWindow Impulse { get; } = new MinuteWindow(candles * candleMinutes);

    public MinuteWindow Last { get; } = new MinuteWindow(candleMinutes);

    public void Add(IdeaCandle minute)
    {
        Impulse.Add(minute);
        Last.Add(minute);
    }
}