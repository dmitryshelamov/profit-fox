using System.Collections.Specialized;
using System.Net;
using System.Web;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>A tiny Bybit v5 stand-in that replaces every outgoing HTTP call of the app, so no test reaches the real Bybit.</summary>
public class FakeBybitHandler : HttpMessageHandler
{
    /// <summary>Instruments Bybit lists, as (category, symbol).</summary>
    public HashSet<(string Category, string Symbol)> Instruments { get; } = [];

    /// <summary>Kline pages served in order, each a list of rows <c>[openTime ISO, open, high, low, close, volume, turnover]</c>, newest first.
    /// Once they run out, Bybit returns empty pages.</summary>
    public Queue<string[][]> KlinePages { get; } = new();

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
            "/v5/market/kline" => Kline(KlinePages.TryDequeue(out string[][]? page) ? page : []),
            _ => throw new InvalidOperationException($"Unexpected Bybit request {request.RequestUri}"),
        };
        var json = $$"""{"retCode":0,"retMsg":"OK","result":{{result}},"retExtInfo":{},"time":0}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private string InstrumentsInfo(string category, string symbol) =>
        Instruments.Contains((category, symbol))
            ? $$"""{"category":"{{category}}","list":[{"symbol":"{{symbol}}","status":"Trading"}]}"""
            : $$"""{"category":"{{category}}","list":[]}""";

    private static string Kline(string[][] rows)
    {
        IEnumerable<string> items = rows.Select(row =>
            $"[\"{DateTimeOffset.Parse(row[0]).ToUnixTimeMilliseconds()}\",{string.Join(",", row.Skip(1).Select(value => $"\"{value}\""))}]");
        return $$"""{"list":[{{string.Join(",", items)}}]}""";
    }
}
