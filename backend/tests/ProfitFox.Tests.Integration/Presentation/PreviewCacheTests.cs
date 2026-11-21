using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfitFox.Application.Preview;
using ProfitFox.Domain.MarketData;
using ProfitFox.Presentation.Preview;
using ProfitFox.Tests.Integration.Database;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     Spec signal-preview «Повторный предпросмотр без чтения истории» (task 3.4): the API on the fake market-data service,
///     which counts the requests it gets, and the cache on its own.
/// </summary>
[NonParallelizable]
public sealed class PreviewCacheTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    [SetUp]
    public void Cover()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
    }

    [TearDown]
    public void Uncover()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_second_preview_of_the_pair_reads_no_history_and_uses_the_new_graph()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market);

        PreviewResponse first = await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));
        int candles = market.Data.CandleRequests.Count;
        int funding = market.Data.FundingRequests.Count;
        JsonObject higher = Signal();
        higher["parameters"]![0]!["value"] = 1;
        PreviewResponse second = await Ok(await Preview(api, "BTCUSDT", "4h", higher));

        Assert.That(candles, Is.GreaterThan(0));
        Assert.That(market.Data.CandleRequests, Has.Count.EqualTo(candles), "no candle request for the second preview");
        Assert.That(market.Data.FundingRequests, Has.Count.EqualTo(funding), "no funding request for the second preview");
        Assert.That(first.Events, Has.Count.EqualTo(28));
        Assert.That(second.Events, Is.Empty, "the threshold 1 is never reached");
        Assert.That(second.Candles, Is.EqualTo(first.Candles));
        Assert.That(second.Coverage, Is.EqualTo(first.Coverage));
    }

    [Test]
    public async Task The_sixth_pair_and_timeframe_pushes_out_the_first()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market);
        (string Pair, string Timeframe)[] reads =
        [
            ("BTCUSDT", "4h"), ("ETHUSDT", "4h"), ("SOLUSDT", "4h"), ("BTCUSDT", "1d"), ("ETHUSDT", "1d"), ("SOLUSDT", "1d")
        ];
        foreach ((string pair, string timeframe) in reads)
        {
            await Ok(await Preview(api, pair, timeframe, Signal()));
        }

        int before = market.Data.CandleRequests.Count;
        await Ok(await Preview(api, "SOLUSDT", "1d", Signal()));
        Assert.That(market.Data.CandleRequests, Has.Count.EqualTo(before), "the last one is still in memory");

        await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));
        Assert.That(market.Data.CandleRequests, Has.Count.GreaterThan(before), "the first one was pushed out");
    }

    [Test]
    public async Task Requests_for_one_key_share_one_read()
    {
        var cache = new PreviewHistoryCache();
        var gate = new TaskCompletionSource();
        int reads = 0;
        Func<Task<PreviewHistory>> read = async () =>
        {
            Interlocked.Increment(ref reads);
            await gate.Task;
            return Empty();
        };

        Task<PreviewHistory> first = cache.Get("btc", TimeSpan.FromHours(4), read, CancellationToken.None);
        Task<PreviewHistory> second = cache.Get("btc", TimeSpan.FromHours(4), read, CancellationToken.None);
        gate.SetResult();

        Assert.That(await second, Is.SameAs(await first));
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public async Task A_failed_read_is_not_kept()
    {
        var cache = new PreviewHistoryCache();
        int reads = 0;

        Assert.ThrowsAsync<HttpRequestException>(() => cache.Get("btc", TimeSpan.FromHours(4), () =>
        {
            reads++;
            throw new HttpRequestException("down");
        }, CancellationToken.None));
        PreviewHistory history = await cache.Get("btc", TimeSpan.FromHours(4), () =>
        {
            reads++;
            return Task.FromResult(Empty());
        }, CancellationToken.None);

        Assert.That(history, Is.Not.Null);
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public async Task A_caller_that_gives_up_does_not_stop_the_read_for_others()
    {
        var cache = new PreviewHistoryCache();
        var gate = new TaskCompletionSource();
        using var giveUp = new CancellationTokenSource();
        Task<PreviewHistory> first = cache.Get("btc", TimeSpan.FromHours(4), async () =>
        {
            await gate.Task;
            return Empty();
        }, giveUp.Token);

        giveUp.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => first);
        gate.SetResult();

        PreviewHistory history = await cache.Get("btc", TimeSpan.FromHours(4), () => throw new InvalidOperationException("read again"), CancellationToken.None);
        Assert.That(history, Is.Not.Null);
    }

    private static PreviewHistory Empty()
    {
        return new PreviewHistory(new HistoryRead<HistoryCandle>([], []), new HistoryRead<HistoryPayment>([], []));
    }

    private static Task<HttpResponseMessage> Preview(BenchPresentationServer api, string instrument, string timeframe, JsonObject signal)
    {
        return api.Http.PostAsJsonAsync("/api/preview", new JsonObject { ["instrument"] = instrument, ["timeframe"] = timeframe, ["signal"] = signal }, Json);
    }

    private static async Task<PreviewResponse> Ok(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PreviewResponse>(Json))!;
    }

    private static JsonObject Signal()
    {
        return JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.AsObject().DeepClone().AsObject();
    }
}
