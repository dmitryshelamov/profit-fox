using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Presentation.Hypotheses;
using ProfitFox.Bench.Presentation.Instruments;
using ProfitFox.Bench.Presentation.Preview;
using ProfitFox.Bench.Hypotheses;
using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.MarketData.History;
using ProfitFox.Bench.Runs;
using ProfitFox.Tests.Integration.Bench.MarketData;
using ProfitFox.Tests.Integration.Bench.Runs;

namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     Spec signal-preview end to end (tasks 2.2 and 2.3): the bench API on the fake market-data service. Its payments
///     cross 0.05 % once a day at 08:00, so H0001 has an event every day at 08:00.
/// </summary>
[NonParallelizable]
public sealed class PreviewTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private string _root = "";
    private string _bench = "";

    [SetUp]
    public void CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        _root = Path.Combine(Path.GetTempPath(), $"profit-fox-preview-{Guid.NewGuid():N}");
        _bench = Path.Combine(_root, "bench");
        string h0001 = Path.Combine(_bench, "hypotheses", "H0001-funding-short-4h");
        Directory.CreateDirectory(h0001);
        File.WriteAllText(Path.Combine(h0001, HypothesisReader.FileName), H0001File());
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task The_pairs_are_those_of_the_bench()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        InstrumentResponse[]? pairs = await api.Http.GetFromJsonAsync<InstrumentResponse[]>("/api/instruments", Json);

        Assert.That(pairs!.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }), "no spot pair");
    }

    [Test]
    public async Task The_hypotheses_are_listed_with_their_signal_or_their_error()
    {
        string broken = Path.Combine(_bench, "hypotheses", "H0002-x");
        Directory.CreateDirectory(broken);
        JsonObject file = JsonNode.Parse(H0001File())!.AsObject();
        file["id"] = "H0002";
        file["name"] = "x";
        file.Remove("signal");
        File.WriteAllText(Path.Combine(broken, HypothesisReader.FileName), file.ToJsonString());
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        HypothesisResponse[]? hypotheses = await api.Http.GetFromJsonAsync<HypothesisResponse[]>("/api/hypotheses", Json);

        Assert.That(hypotheses!.Select(hypothesis => hypothesis.Id), Is.EqualTo(new[] { "H0001", "H0002" }));
        HypothesisResponse h0001 = hypotheses![0];
        Assert.That((h0001.Name, h0001.Timeframe, h0001.Error), Is.EqualTo(("funding-short-4h", "4h", (string?)null)));
        Assert.That(h0001.Signal!.Value.GetProperty("nodes").GetArrayLength(), Is.EqualTo(5));
        HypothesisResponse h0002 = hypotheses[1];
        Assert.That(h0002.Name, Is.EqualTo("x"));
        Assert.That(h0002.Signal, Is.Null);
        Assert.That(h0002.Error, Does.Contain("signal"));
    }

    [Test]
    public async Task The_preview_of_H0001_gives_the_events_of_a_bench_run()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        PreviewResponse preview = await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));

        Assert.That(preview.Candles, Has.Count.GreaterThan(100));
        Assert.That(preview.Coverage.First().From, Is.EqualTo(FakeMarketData.CoveredFrom.ToUnixTimeMilliseconds()));
        Assert.That(preview.PeriodEnd, Is.EqualTo(Periods.ExplorationEnd.ToUnixTimeMilliseconds()));
        Assert.That(preview.Events, Has.Count.EqualTo(28), "every day of the four weeks at 08:00");
        RunResult run = await RunParity(market);
        Assert.That(
            preview.Events.Select(item => DateTimeOffset.FromUnixTimeMilliseconds(item.Moment)),
            Is.EqualTo(run.Rows.Where(row => row.Instrument == "BTCUSDT").Select(row => row.Moment).Order()));
    }

    [Test]
    public async Task A_signal_that_never_fires_gives_candles_and_no_events()
    {
        JsonObject signal = Signal();
        signal["parameters"]![0]!["value"] = 1;
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        PreviewResponse preview = await Ok(await Preview(api, "ETHUSDT", "4h", signal));

        Assert.That(preview.Candles, Is.Not.Empty);
        Assert.That(preview.Events, Is.Empty);
    }

    [Test]
    public async Task Nothing_after_exploration_is_read()
    {
        FakeMarketData.CoveredUntil = null;
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        PreviewResponse preview = await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));

        DateTimeOffset end = Periods.ExplorationEnd;
        Assert.That(DateTimeOffset.FromUnixTimeMilliseconds(preview.Candles[^1].OpenTime), Is.EqualTo(end.AddHours(-4)), "the last candle closes at the end");
        Assert.That(market.Data.CandleRequests, Has.All.Matches<ProfitFox.Bench.MarketData.Protos.CandleHistoryRequest>(request => request.To.ToDateTimeOffset() <= end));
        Assert.That(
            market.Data.FundingRequests,
            Has.All.Matches<ProfitFox.Bench.MarketData.Protos.FundingPaymentHistoryRequest>(request => request.To.ToDateTimeOffset() <= end.AddSeconds(1)),
            "the payment exactly at the end is read, as by a run of exploration");
    }

    [Test]
    public async Task A_wire_to_a_missing_node_is_refused_before_any_read()
    {
        JsonObject signal = Signal();
        signal["wires"]![0]!["to"] = "hgh.a";
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        ErrorResponse refused = await BadRequest(await Preview(api, "BTCUSDT", "4h", signal));

        Assert.That(refused.Errors, Has.Some.Matches<RequestError>(error => error.Wire == new RequestWire("rate.value", "hgh.a")));
        Assert.That(market.Data.CandleRequests, Is.Empty);
        Assert.That(market.Data.FundingRequests, Is.Empty);
    }

    [Test]
    public async Task An_unknown_pair_and_timeframe_are_refused_before_any_read()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        ErrorResponse refused = await BadRequest(await Preview(api, "NOSUCHUSDT", "2h", Signal()));

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field)),
            Is.EquivalentTo(new[] { ("InvalidTimeframe", (string?)"timeframe"), ("UnknownInstrument", "instrument") }));
        Assert.That(market.Data.CandleRequests, Is.Empty);
    }

    [Test]
    public async Task The_preview_leaves_no_trace_in_the_bench()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, _bench);

        await Ok(await Preview(api, "SOLUSDT", "4h", Signal()));

        Assert.That(File.Exists(Path.Combine(_bench, "ledger.jsonl")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_bench, "reports")), Is.False);
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

    private static async Task<ErrorResponse> BadRequest(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
    }

    // A parity run of H0001 on the same fake history, in a bench folder of its own.
    private async Task<RunResult> RunParity(MarketDataServer market)
    {
        string bench = Path.Combine(_root, "run", "bench");
        string hypothesis = Path.Combine(bench, "hypotheses", "H0001-funding-short-4h");
        Directory.CreateDirectory(hypothesis);
        File.WriteAllText(Path.Combine(hypothesis, HypothesisReader.FileName), H0001File());
        var catalog = new InstrumentCatalog(market.Http);
        var runner = new HypothesisRunner(new GrpcHistorySource(market.Channel), catalog.LinearUsdtPerpetuals, new FakeRepository(), bench, TextWriter.Null);
        var options = new RunOptions(hypothesis, Period.Exploration, Parity: true, Path.Combine(_root, "dump"), new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        return await runner.Run(options, CancellationToken.None);
    }

    private static JsonObject Signal()
    {
        return JsonNode.Parse(H0001File())!["signal"]!.AsObject().DeepClone().AsObject();
    }

    private static string H0001File()
    {
        DirectoryInfo? folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return File.ReadAllText(Path.Combine(folder!.FullName, "bench", "hypotheses", "H0001-funding-short-4h", HypothesisReader.FileName));
    }
}
