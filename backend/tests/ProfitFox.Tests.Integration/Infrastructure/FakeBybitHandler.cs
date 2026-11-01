using System.Collections.Specialized;
using System.Net;
using System.Web;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>A tiny Bybit v5 stand-in that replaces every outgoing HTTP call of the app, so no test reaches the real Bybit.</summary>
public class FakeBybitHandler : HttpMessageHandler
{
    /// <summary>Instruments Bybit lists, as (category, symbol).</summary>
    public HashSet<(string Category, string Symbol)> Instruments { get; } = [];

    /// <summary>Price and lot filters of listed instruments, as Bybit writes them; an instrument without them is listed bare.</summary>
    public Dictionary<(string Category, string Symbol), (string TickSize, string QuantityStep, string MinQuantity, string MinAmount)> Rules { get; } = [];

    /// <summary>
    ///     The candle history Bybit knows, rows <c>[openTime ISO, open, high, low, close, volume, turnover]</c> in any order. A kline
    ///     request gets the rows with open time in its inclusive <c>[start, end]</c>, newest first, at most <c>limit</c> of them.
    /// </summary>
    public List<string[]> Klines { get; } = [];

    /// <summary>The <c>[start, end]</c> of every kline request, in order.</summary>
    public List<(DateTimeOffset Start, DateTimeOffset End)> KlineRequests { get; } = [];

    public bool Unavailable { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        NameValueCollection query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        string result = request.RequestUri.AbsolutePath switch
        {
            "/v5/market/instruments-info" => InstrumentsInfo(query["category"]!, query["symbol"]!),
            "/v5/market/kline" => Kline(query),
            _ => throw new InvalidOperationException($"Unexpected Bybit request {request.RequestUri}")
        };
        var json = $$"""{"retCode":0,"retMsg":"OK","result":{{result}},"retExtInfo":{},"time":0}""";

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private string InstrumentsInfo(string category, string symbol)
    {
        if (!Instruments.Contains((category, symbol)))
        {
            return $$"""{"category":"{{category}}","list":[]}""";
        }

        string filters = !Rules.TryGetValue((category, symbol), out (string TickSize, string QuantityStep, string MinQuantity, string MinAmount) rules) ? ""
            : category == "spot"
                ? $$""","priceFilter":{"tickSize":"{{rules.TickSize}}"},"lotSizeFilter":{"basePrecision":"{{rules.QuantityStep}}","quotePrecision":"0.00000001","minOrderQty":"{{rules.MinQuantity}}","maxOrderQty":"1000","minOrderAmt":"{{rules.MinAmount}}","maxOrderAmt":"2000000"}"""
                : $$""","priceFilter":{"minPrice":"0.1","maxPrice":"1999999","tickSize":"{{rules.TickSize}}"},"lotSizeFilter":{"maxOrderQty":"100","minOrderQty":"{{rules.MinQuantity}}","qtyStep":"{{rules.QuantityStep}}","minNotionalValue":"{{rules.MinAmount}}"}""";

        return $$"""{"category":"{{category}}","list":[{"symbol":"{{symbol}}","status":"Trading"{{filters}}}]}""";
    }

    private string Kline(NameValueCollection query)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["start"]!));
        var end = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["end"]!));
        KlineRequests.Add((start, end));
        IEnumerable<string> items = Klines
            .Select(row => (OpenTime: DateTimeOffset.Parse(row[0]), Row: row))
            .Where(candle => candle.OpenTime >= start && candle.OpenTime <= end)
            .OrderByDescending(candle => candle.OpenTime)
            .Take(int.Parse(query["limit"]!))
            .Select(candle => $"[\"{candle.OpenTime.ToUnixTimeMilliseconds()}\",{string.Join(",", candle.Row.Skip(1).Select(value => $"\"{value}\""))}]");

        return $$"""{"list":[{{string.Join(",", items)}}]}""";
    }
}