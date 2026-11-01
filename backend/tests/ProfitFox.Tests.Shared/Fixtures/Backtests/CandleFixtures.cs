using System.Text.Json;

namespace ProfitFox.Tests.Shared.Fixtures.Backtests;

/// <summary>Reference candle sets of the backtests: rows of open time, open, high, low, close, volume and turnover as Bybit reports them.</summary>
public static class CandleFixtures
{
    /// <summary>24 hourly candles from 2024-03-01T00:00:00Z that go down, up, down and up between 95 and 104.</summary>
    public static IReadOnlyList<string[]> Hourly()
    {
        using Stream stream = typeof(CandleFixtures).Assembly.GetManifestResourceStream("ProfitFox.Tests.Shared.Fixtures.Backtests.hourly.json")!;
        using var document = JsonDocument.Parse(stream);

        return
        [
            .. document.RootElement.EnumerateArray().Select(row => new[]
            {
                row.GetProperty("openTime").GetString()!,
                row.GetProperty("open").GetString()!,
                row.GetProperty("high").GetString()!,
                row.GetProperty("low").GetString()!,
                row.GetProperty("close").GetString()!,
                row.GetProperty("volume").GetString()!,
                row.GetProperty("turnover").GetString()!
            })
        ];
    }
}