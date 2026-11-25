using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.MarketData;

using static ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache.HistoryCacheFolder;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-backtest-history-cache, task 4.3: both previews through the cache of history of the API — spec
///     market-history-cache «Предпросмотр после перезапуска» and spec strategy-preview «Только разведка» with a folder of the
///     cache. The clock is that of 2026-05-16 09:00: exploration ends at 2026-05-16, in the open month; the history of the
///     fake starts on 2026-01-05, so February to April are the closed months covered whole.
/// </summary>
[NonParallelizable]
public sealed class PreviewHistoryCacheTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset February = Day(2026, 2, 1);
    private static readonly DateTimeOffset May = Day(2026, 5, 1);
    private HistoryCacheFolder _folder = null!;

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = null;
        await BenchDatabase.Reset();
        _folder = new HistoryCacheFolder();
    }

    [TearDown]
    public void RemoveBench()
    {
        _folder.Dispose();
    }

    [Test]
    public async Task The_preview_of_a_signal_after_a_restart_of_the_API_reads_the_closed_months_from_the_folder()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        string first;
        await using (var api = new BenchTestApplication(market, time: FixedTime.Exploration, cache: _folder.Path))
        {
            first = await Ok(await PreviewSignal(api));
        }

        Clear(market);
        await using var restarted = new BenchTestApplication(market, time: FixedTime.Exploration, cache: _folder.Path);

        string again = await Ok(await PreviewSignal(restarted));

        Assert.That(again, Is.EqualTo(first));
        AssertOnlyTheOpenMonthAndTheListingAreAsked(market);
    }

    [Test]
    public async Task The_preview_of_a_strategy_after_a_restart_of_the_API_reads_the_closed_months_from_the_folder()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        string first;
        Guid signal;
        await using (var api = new BenchTestApplication(market, time: FixedTime.Exploration, cache: _folder.Path))
        {
            signal = await SaveSignal(api);
            first = await Ok(await PreviewStrategy(api, signal));
        }

        Clear(market);
        await using var restarted = new BenchTestApplication(market, time: FixedTime.Exploration, cache: _folder.Path);

        string again = await Ok(await PreviewStrategy(restarted, signal));

        Assert.That(again, Is.EqualTo(first));
        AssertOnlyTheOpenMonthAndTheListingAreAsked(market);
        Assert.That(_folder.Months.Select(path => Path.GetRelativePath(_folder.Path, path)), Does.Contain(Path.Combine("btc", "candles-4h", "2026-04.gz")).And.Contain(Path.Combine("btc", "funding", "2026-02.gz")));
    }

    /// <summary>
    ///     Spec strategy-preview «Только разведка» and signal-preview the same with a folder of the cache: nothing after the end
    ///     of exploration is asked, the first time or from the cache, and the answer is that of an API without the cache.
    /// </summary>
    [Test]
    public async Task With_a_folder_of_the_cache_nothing_after_exploration_is_asked_and_the_previews_are_those_without_it()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        string plainSignal;
        string plainStrategy;
        Guid signal;
        await using (var plain = new BenchTestApplication(market, time: FixedTime.Exploration))
        {
            signal = await SaveSignal(plain);
            plainSignal = await Ok(await PreviewSignal(plain));
            plainStrategy = await Ok(await PreviewStrategy(plain, signal));
        }

        await using var api = new BenchTestApplication(market, time: FixedTime.Exploration, cache: _folder.Path);
        Clear(market);

        Assert.That(await Ok(await PreviewSignal(api)), Is.EqualTo(plainSignal));
        Assert.That(await Ok(await PreviewStrategy(api, signal)), Is.EqualTo(plainStrategy));
        Assert.That(await Ok(await PreviewStrategy(api, signal, "ETHUSDT")), Is.Not.Empty);

        DateTimeOffset end = FixedTime.ExplorationEnd;
        Assert.That(market.Data.CandleRequests, Is.Not.Empty);
        Assert.That(market.Data.CandleRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(end));
        Assert.That(market.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(end));
        Assert.That(_folder.Months, Is.Not.Empty);
    }

    // The API asked the service for the history before the first whole month, which is not kept, and for the open month.
    private static void AssertOnlyTheOpenMonthAndTheListingAreAsked(MarketDataServer market)
    {
        (DateTimeOffset From, DateTimeOffset To)[] asked =
        [
            .. market.Data.CandleRequests.Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset())),
            .. market.Data.FundingRequests.Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset()))
        ];
        Assert.That(asked, Has.Some.Matches<(DateTimeOffset From, DateTimeOffset To)>(window => window.From == May && window.To == FixedTime.ExplorationEnd), "the part of the window in May");
        Assert.That(asked, Has.All.Matches<(DateTimeOffset From, DateTimeOffset To)>(window => window.To <= February || window.From >= May), "February to April come from the folder");
    }

    private static void Clear(MarketDataServer market)
    {
        market.Data.CandleRequests.Clear();
        market.Data.FundingRequests.Clear();
    }

    private static Task<HttpResponseMessage> PreviewSignal(BenchTestApplication api)
    {
        return api.Http.PostAsJsonAsync("/api/preview", new JsonObject { ["instrument"] = "BTCUSDT", ["timeframe"] = "4h", ["signal"] = ReferenceSignals.H0001() }, Json);
    }

    private static Task<HttpResponseMessage> PreviewStrategy(BenchTestApplication api, Guid signal, string instrument = "BTCUSDT")
    {
        return api.Http.PostAsJsonAsync("/api/preview/strategy", new JsonObject { ["instrument"] = instrument, ["timeframe"] = "4h", ["strategy"] = StrategiesTests.Strategy(signal) }, Json);
    }

    // The signal of H0001 saved as funding-high.
    private static async Task<Guid> SaveSignal(BenchTestApplication api)
    {
        HttpResponseMessage created = await api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = "funding-high", ["graph"] = ReferenceSignals.H0001(), ["layout"] = new JsonObject() }, Json);
        return (await created.Content.ReadFromJsonAsync<ProfitFox.Presentation.Contracts.Signals.SignalResponse>(Json))!.Id;
    }

    private static async Task<string> Ok(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        return body;
    }
}
