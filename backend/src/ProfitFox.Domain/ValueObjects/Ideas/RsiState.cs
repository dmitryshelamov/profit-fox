namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The smoothing of Wilder after the closed candles of an interval: the average gain and loss of a change and the last close. RSI is
///     100 − 100 / (1 + gain / loss); a price that never moved has none.
/// </summary>
public sealed record RsiState(decimal Gain, decimal Loss, decimal Close)
{
    private const int Period = 14;

    public decimal? Value => Of(Gain, Loss);

    /// <summary>RSI with <paramref name="close" /> as the close of the next candle: one more step of the smoothing.</summary>
    public decimal? With(decimal close)
    {
        decimal change = close - Close;

        return Of(Smooth(Gain, Math.Max(change, 0m)), Smooth(Loss, Math.Max(-change, 0m)));
    }

    /// <summary>The state after one more candle closed at <paramref name="close" />.</summary>
    public RsiState Next(decimal close)
    {
        decimal change = close - Close;

        return new RsiState(Smooth(Gain, Math.Max(change, 0m)), Smooth(Loss, Math.Max(-change, 0m)), close);
    }

    private static decimal Smooth(decimal average, decimal value)
    {
        return ((average * (Period - 1)) + value) / Period;
    }

    private static decimal? Of(decimal gain, decimal loss)
    {
        if (loss == 0)
        {
            return gain == 0 ? null : 100m;
        }

        return 100m - (100m / (1m + (gain / loss)));
    }
}