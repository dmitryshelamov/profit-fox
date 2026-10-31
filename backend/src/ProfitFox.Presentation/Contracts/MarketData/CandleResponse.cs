using System.Globalization;

using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Presentation.Contracts.MarketData;

/// <summary>Prices and volumes as decimal strings: JSON numbers lose precision in JavaScript clients.</summary>
public sealed record CandleResponse(
    DateTimeOffset OpenTime,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume,
    string Turnover)
{
    public static CandleResponse Of(Candle candle) => new(
        candle.OpenTime,
        Format(candle.Open.Value),
        Format(candle.High.Value),
        Format(candle.Low.Value),
        Format(candle.Close.Value),
        Format(candle.Volume.Value),
        Format(candle.Turnover.Value));

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
