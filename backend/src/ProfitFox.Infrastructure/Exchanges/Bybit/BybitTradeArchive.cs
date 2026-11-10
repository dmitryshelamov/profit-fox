using System.Globalization;
using System.IO.Compression;
using System.Net;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The daily archive of trades of Bybit (decision 8 of collect-trade-flow): a static host, not the API, so its client has no limiter of
///     Bybit REST. A file is read line by line as it is downloaded and decompressed. <c>linear</c>:
///     <c>trading/S/S2026-11-09.csv.gz</c>, the time in seconds with a fraction, <c>Buy</c> / <c>Sell</c>, <c>size</c>; <c>spot</c>:
///     <c>spot/S/S_2026-11-09.csv.gz</c>, the time in milliseconds, <c>buy</c> / <c>sell</c>, <c>volume</c>. Every trade counts, as in the
///     stream (decision 5).
/// </summary>
internal sealed class BybitTradeArchive(HttpClient client) : ITradeArchive
{
    /// <summary>How long the download of one file may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public async Task<bool> ReadDayAsync(Instrument instrument, DateTimeOffset day, Action<TradeEvent> trade, CancellationToken cancellationToken)
    {
        bool linear = instrument.Category == MarketCategory.Linear;
        string date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string path = linear ? $"trading/{instrument.Symbol}/{instrument.Symbol}{date}.csv.gz" : $"spot/{instrument.Symbol}/{instrument.Symbol}_{date}.csv.gz";
        using HttpResponseMessage response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ExchangeUnavailableException($"The archive of Bybit answered {(int)response.StatusCode} for {path}.");
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var gzip = new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        string[] header = (await reader.ReadLineAsync(cancellationToken))!.Split(',');
        int time = Array.IndexOf(header, "timestamp");
        int side = Array.IndexOf(header, "side");
        int quantity = Array.IndexOf(header, linear ? "size" : "volume");
        int price = Array.IndexOf(header, "price");

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            string[] fields = line.Split(',');
            trade(
                new TradeEvent(
                    linear ? FromSeconds(fields[time]) : DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(fields[time], CultureInfo.InvariantCulture)),
                    string.Equals(fields[side], "buy", StringComparison.OrdinalIgnoreCase) ? TradeSide.Buy : TradeSide.Sell,
                    decimal.Parse(fields[quantity], CultureInfo.InvariantCulture),
                    decimal.Parse(fields[price], CultureInfo.InvariantCulture)));
        }

        return true;
    }

    // As a decimal, not a double: 1794225899.999 seconds is 12:04:59.999 exactly, never 12:05:00.
    private static DateTimeOffset FromSeconds(string seconds)
    {
        return DateTimeOffset.UnixEpoch.AddTicks((long)(decimal.Parse(seconds, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond));
    }
}