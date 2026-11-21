using System.Globalization;

using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.MarketData;

/// <summary>
///     A candle as the market-data service tells it: prices and volumes as decimal text, exactly as stored.
/// </summary>
public sealed record HistoryCandle(
    DateTimeOffset OpenTime,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume,
    string? Turnover,
    string? BuyVolume)
{
    public bool HasTrades => Parse(Volume) > 0;

    public ClosedCandle ToClosedCandle(TimeSpan interval)
    {
        return new ClosedCandle(OpenTime, OpenTime + interval, Parse(Open), Parse(High), Parse(Low), Parse(Close), Parse(Volume));
    }

    private static decimal Parse(string text)
    {
        return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
