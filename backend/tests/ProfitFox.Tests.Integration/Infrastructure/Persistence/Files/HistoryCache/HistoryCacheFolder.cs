using System.Globalization;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache;

/// <summary>
///     A folder of the cache of history for one test, under the temporary folder of the machine and never in the home folder,
///     and months of history to put in it.
/// </summary>
internal sealed class HistoryCacheFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "profit-fox-tests", "history-cache", Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     The files of months in the folder.
    /// </summary>
    public string[] Months => Directory.Exists(Path) ? Directory.GetFiles(Path, "*.gz", SearchOption.AllDirectories) : [];

    /// <summary>
    ///     The temporary files in the folder.
    /// </summary>
    public string[] Temporaries => Directory.Exists(Path) ? Directory.GetFiles(Path, "*.tmp", SearchOption.AllDirectories) : [];

    public static DateTimeOffset Day(int year, int month, int day)
    {
        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    ///     The candles of <paramref name="count" /> minutes from the start of the month, every one covered, with prices and
    ///     volumes as the service writes them and some turnovers and buy volumes missing.
    /// </summary>
    public static HistoryRead<HistoryCandle> Minutes(HistoryMonth month, int count, int seed = 1)
    {
        var random = new Random(seed);
        decimal price = 60_000m + random.Next(10_000);
        List<HistoryCandle> candles = [];
        for (int minute = 0; minute < count; minute++)
        {
            decimal open = price;
            price = Math.Max(1m, price + (random.Next(-400, 401) / 10m));
            decimal high = Math.Max(open, price) + (random.Next(0, 200) / 10m);
            decimal low = Math.Min(open, price) - (random.Next(0, 200) / 10m);
            decimal volume = random.Next(0, 5) == 0 ? 0m : random.Next(1, 2_000_000) / 1000m;
            candles.Add(new HistoryCandle(
                month.Start.AddMinutes(minute),
                Text(open),
                Text(high),
                Text(low),
                Text(price),
                Text(volume),
                minute % 9 == 0 ? null : Text(Math.Round(volume * price, 4)),
                minute % 11 == 0 ? null : Text(Math.Round(volume * random.Next(0, 101) / 100m, 3))));
        }

        return new HistoryRead<HistoryCandle>([new TimeRange(month.Start, month.End)], candles);
    }

    /// <summary>
    ///     A month whose file takes about <paramref name="megabytes" />: candles of random text, which GZip cannot make smaller.
    /// </summary>
    public static HistoryRead<HistoryCandle> Noise(HistoryMonth month, double megabytes, int seed)
    {
        var random = new Random(seed);
        const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string Word()
        {
            return string.Create(20, random, (span, from) =>
            {
                for (int index = 0; index < span.Length; index++)
                {
                    span[index] = Letters[from.Next(Letters.Length)];
                }
            });
        }

        // A candle of eight words of 20 letters of 62 takes about 120 bytes once compressed.
        int count = (int)(megabytes * 1024 * 1024 / 120);
        List<HistoryCandle> candles = [];
        for (int minute = 0; minute < count; minute++)
        {
            candles.Add(new HistoryCandle(month.Start.AddMinutes(minute), Word(), Word(), Word(), Word(), Word(), Word(), Word()));
        }

        return new HistoryRead<HistoryCandle>([new TimeRange(month.Start, month.End)], candles);
    }

    public static HistoryRead<HistoryPayment> Funding(HistoryMonth month)
    {
        List<HistoryPayment> payments = [];
        for (DateTimeOffset time = month.Start; time < month.End; time = time.AddHours(8))
        {
            payments.Add(new HistoryPayment(time, ((time.Day % 3 - 1) * 0.0001m).ToString(CultureInfo.InvariantCulture)));
        }

        return new HistoryRead<HistoryPayment>([new TimeRange(month.Start, month.End)], payments);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
