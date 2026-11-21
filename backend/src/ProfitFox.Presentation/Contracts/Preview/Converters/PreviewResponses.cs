using System.Globalization;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Presentation.Contracts.Preview.Converters;

/// <summary>
///     The preview as the API tells it: times in Unix milliseconds UTC, prices as numbers.
/// </summary>
public static class PreviewResponses
{
    public static PreviewResponse Of(SignalPreviewResult preview)
    {
        return new PreviewResponse(
            preview.Instrument.Symbol,
            Timeframes.Name(preview.Timeframe),
            [.. preview.Candles.Select(Candle)],
            [.. preview.Coverage.Select(range => new PreviewRange(Ms(range.From), Ms(range.Until)))],
            [.. preview.Events.Select(moment => new PreviewEvent(Ms(moment)))],
            Ms(preview.PeriodEnd));
    }

    private static PreviewCandle Candle(HistoryCandle candle)
    {
        return new PreviewCandle(Ms(candle.OpenTime), Number(candle.Open), Number(candle.High), Number(candle.Low), Number(candle.Close), Number(candle.Volume));
    }

    private static double Number(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static long Ms(DateTimeOffset time)
    {
        return time.ToUnixTimeMilliseconds();
    }
}
