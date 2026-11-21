using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProfitFox.Domain.MarketData;

/// <summary>
///     The hash of everything a run read (spec market-history): every read with its coverage and items, field by field in
///     the order they came. Covered history never changes, so reading the same windows again gives the same hash.
/// </summary>
public sealed class HistoryHash : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void Add(string instrumentId, TimeSpan interval, HistoryRead<HistoryCandle> read)
    {
        Line("candles", instrumentId, ((long)interval.TotalMinutes).ToString(CultureInfo.InvariantCulture));
        AddCoverage(read.Coverage);
        foreach (HistoryCandle candle in read.Items)
        {
            Line(Ticks(candle.OpenTime), candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.Turnover ?? "", candle.BuyVolume ?? "");
        }
    }

    public void Add(string instrumentId, HistoryRead<HistoryPayment> read)
    {
        Line("funding", instrumentId);
        AddCoverage(read.Coverage);
        foreach (HistoryPayment payment in read.Items)
        {
            Line(Ticks(payment.Time), payment.Rate);
        }
    }

    public string Result()
    {
        return Convert.ToHexStringLower(_hash.GetCurrentHash());
    }

    public void Dispose()
    {
        _hash.Dispose();
    }

    private void AddCoverage(IReadOnlyList<TimeRange> coverage)
    {
        Line([.. coverage.SelectMany(range => new[] { Ticks(range.From), Ticks(range.Until) }).Prepend("coverage")]);
    }

    private void Line(params string[] fields)
    {
        _hash.AppendData(Encoding.UTF8.GetBytes(string.Join('\u001f', fields) + "\n"));
    }

    private static string Ticks(DateTimeOffset time)
    {
        return time.UtcTicks.ToString(CultureInfo.InvariantCulture);
    }
}
