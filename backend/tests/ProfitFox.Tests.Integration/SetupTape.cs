using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     Candles of one pair for the tests of the setups, one after another from Monday 2026-01-05 on a timeframe of 1h unless
///     told otherwise. A steady candle opens and closes at its price with its range around it, so the true range of each is
///     its range and the ATR of a steady run is that range; the volume of a steady candle is 1000 unless told otherwise.
/// </summary>
internal sealed class SetupTape(TimeSpan? timeframe = null)
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    private readonly List<ClosedCandle> _candles = [];

    public TimeSpan Timeframe { get; } = timeframe ?? TimeSpan.FromHours(1);

    public IReadOnlyList<ClosedCandle> Candles => _candles;

    public ClosedCandle Last => _candles[^1];

    public SetupTape Steady(int count, decimal price, decimal range, decimal volume = 1000)
    {
        for (int index = 0; index < count; index++)
        {
            Add(price, price + (range / 2), price - (range / 2), price, volume);
        }

        return this;
    }

    public SetupTape Add(decimal open, decimal high, decimal low, decimal close, decimal volume = 1000)
    {
        DateTimeOffset openTime = Start + (_candles.Count * Timeframe);
        _candles.Add(new ClosedCandle(openTime, openTime + Timeframe, open, high, low, close, volume));
        return this;
    }
}
