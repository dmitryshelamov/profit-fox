using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Commands.Hypotheses.AddHypothesis;
using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Hypotheses;
using ProfitFox.Presentation.Contracts.Instruments;
using ProfitFox.Presentation.Contracts.Preview;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Spec signal-preview end to end (tasks 2.2 and 2.3) and spec strategy-preview (change add-strategy-constructor,
///     task 5.3): the bench API on the fake market-data service. Its payments
///     cross 0.05 % once a day at 08:00, so H0001 has an event every day at 08:00.
/// </summary>
[NonParallelizable]
public sealed class PreviewTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        await BenchServices.Sender.Send(new AddHypothesisCommand(H0001File()), CancellationToken.None);
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task The_pairs_are_those_of_the_bench()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        InstrumentResponse[]? pairs = await api.Http.GetFromJsonAsync<InstrumentResponse[]>("/api/instruments", Json);

        Assert.That(pairs!.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }), "no spot pair");
    }

    [Test]
    public async Task The_hypotheses_are_listed_with_their_signal_or_their_error()
    {
        // A text that cannot be loaded, as one the engine no longer reads would be: it goes in below the store.
        JsonObject file = JsonNode.Parse(H0001File())!.AsObject();
        file["id"] = "H0002";
        file["name"] = "x";
        file.Remove("signal");
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO hypothesis_versions (hypothesis_id, version, hash, text) VALUES ('H0002', 1, 'broken', @Text)",
                new { Text = file.ToJsonString() });
        }
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        HypothesisResponse[]? hypotheses = await api.Http.GetFromJsonAsync<HypothesisResponse[]>("/api/hypotheses", Json);

        Assert.That(hypotheses!.Select(hypothesis => hypothesis.Id), Is.EqualTo(new[] { "H0001", "H0002" }));
        HypothesisResponse h0001 = hypotheses![0];
        Assert.That((h0001.Name, h0001.Version, h0001.Timeframe, h0001.Error), Is.EqualTo(("funding-short-4h", 1, "4h", (string?)null)));
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
        await using var api = new BenchTestApplication(market);

        PreviewResponse preview = await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));

        Assert.That(preview.Candles, Has.Count.GreaterThan(100));
        Assert.That(preview.Coverage.First().From, Is.EqualTo(FakeMarketData.CoveredFrom.ToUnixTimeMilliseconds()));
        Assert.That(preview.PeriodEnd, Is.EqualTo(Periods.ExplorationEnd.ToUnixTimeMilliseconds()));
        Assert.That(preview.Events, Has.Count.EqualTo(28), "every day of the four weeks at 08:00");
        ExecuteRunResult run = await RunParity(market);
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
        await using var api = new BenchTestApplication(market);

        PreviewResponse preview = await Ok(await Preview(api, "ETHUSDT", "4h", signal));

        Assert.That(preview.Candles, Is.Not.Empty);
        Assert.That(preview.Events, Is.Empty);
    }

    [Test]
    public async Task Nothing_after_exploration_is_read()
    {
        FakeMarketData.CoveredUntil = null;
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        PreviewResponse preview = await Ok(await Preview(api, "BTCUSDT", "4h", Signal()));

        DateTimeOffset end = Periods.ExplorationEnd;
        Assert.That(DateTimeOffset.FromUnixTimeMilliseconds(preview.Candles[^1].OpenTime), Is.EqualTo(end.AddHours(-4)), "the last candle closes at the end");
        Assert.That(market.Data.CandleRequests, Has.All.Matches<ProfitFox.Infrastructure.Clients.MarketData.Protos.CandleHistoryRequest>(request => request.To.ToDateTimeOffset() <= end));
        Assert.That(
            market.Data.FundingRequests,
            Has.All.Matches<ProfitFox.Infrastructure.Clients.MarketData.Protos.FundingPaymentHistoryRequest>(request => request.To.ToDateTimeOffset() <= end.AddSeconds(1)),
            "the payment exactly at the end is read, as by a run of exploration");
    }

    [Test]
    public async Task A_wire_to_a_missing_node_is_refused_before_any_read()
    {
        JsonObject signal = Signal();
        signal["wires"]![0]!["to"] = "hgh.a";
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        ErrorResponse refused = await BadRequest(await Preview(api, "BTCUSDT", "4h", signal));

        Assert.That(refused.Errors, Has.Some.Matches<RequestError>(error => error.Wire == new RequestWire("rate.value", "hgh.a")));
        Assert.That(market.Data.CandleRequests, Is.Empty);
        Assert.That(market.Data.FundingRequests, Is.Empty);
    }

    [Test]
    public async Task An_unknown_pair_and_timeframe_are_refused_before_any_read()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        ErrorResponse refused = await BadRequest(await Preview(api, "NOSUCHUSDT", "2h", Signal()));

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field)),
            Is.EquivalentTo(new[] { ("InvalidTimeframe", (string?)"timeframe"), ("UnknownInstrument", "instrument") }));
        Assert.That(market.Data.CandleRequests, Is.Empty);
    }

    [Test]
    public async Task The_preview_leaves_no_trace_in_the_bench()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        await Ok(await Preview(api, "SOLUSDT", "4h", Signal()));

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT (SELECT count(*) FROM ledger) + (SELECT count(*) FROM reports) + (SELECT count(*) FROM runs)"), Is.Zero);
    }

    /// <summary>
    ///     Spec strategy-preview «Те же сделки, что у стенда» (change add-strategy-constructor, task 5.3).
    /// </summary>
    [Test]
    public async Task The_trades_of_H0001_are_those_of_a_run()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);
        Guid signal = await SaveSignal(api);

        StrategyPreviewResponse preview = await OkStrategy(await PreviewStrategy(api, "BTCUSDT", "4h", StrategiesTests.Strategy(signal)));
        ExecuteRunResult run = await RunWithOutcomes(market);

        (long, double, double, double)[] events =
        [
            .. run.Rows
                .Where(row => row.Instrument == "BTCUSDT" && row.Outcome is { Status: TradeStatus.Ok })
                .OrderBy(row => row.Moment)
                .Select(row => (row.Moment.ToUnixTimeMilliseconds(), row.Outcome!.EntryPrice!.Value, row.Outcome.ExitPrice!.Value, row.Outcome.Net!.Value))
        ];
        Assert.That(events, Has.Length.EqualTo(28));
        Assert.That(preview.Orders.Select(order => (order.Side, order.Outcome)), Has.All.EqualTo(("short", "opened")));
        Assert.That(preview.Orders.Select(order => (order.Moment, order.Trade!.EntryPrice, order.Trade.ExitPrice, order.Trade.Net)), Is.EqualTo(events));
        Assert.That(preview.Orders.Select(order => order.Trade!.ExitReason), Has.All.EqualTo("time"));
        Assert.That(preview.ComputedFrom, Is.Null);
    }

    /// <summary>
    ///     Spec strategy-preview «Предпросмотр стратегии без следов стенда» (change add-strategy-constructor, task 5.3).
    /// </summary>
    [Test]
    public async Task The_journal_after_a_preview_of_a_strategy()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);
        Guid signal = await SaveSignal(api);
        HttpResponseMessage saved = await api.Http.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = "funding-short", ["graph"] = StrategiesTests.Strategy(signal), ["layout"] = new JsonObject() }, Json);
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        await OkStrategy(await PreviewStrategy(api, "SOLUSDT", "4h", StrategiesTests.Strategy(signal, after: "8h")));

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT (SELECT count(*) FROM ledger) + (SELECT count(*) FROM reports) + (SELECT count(*) FROM runs)"), Is.Zero);
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT (SELECT count(*) FROM strategy_versions) + (SELECT count(*) FROM signal_versions)"), Is.EqualTo(2), "nothing saved changed");
    }

    [Test]
    public async Task A_strategy_without_close_after_on_an_unknown_pair_is_refused_before_any_read()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);
        Guid signal = await SaveSignal(api);
        JsonObject graph = StrategiesTests.Strategy(signal);
        StrategiesTests.RemoveCloseAfter(graph);

        HttpResponseMessage response = await PreviewStrategy(api, "NOSUCHUSDT", "4h", graph);

        ErrorResponse refused = await BadRequest(response);
        Assert.That(refused.Errors.Select(error => error.Code), Is.EquivalentTo(new[] { "StrategyExitMissing", "UnknownInstrument" }));
        Assert.That(market.Data.CandleRequests, Is.Empty);
        Assert.That(market.Data.FundingRequests, Is.Empty);
    }

    private static Task<HttpResponseMessage> PreviewStrategy(BenchTestApplication api, string instrument, string timeframe, JsonObject strategy)
    {
        return api.Http.PostAsJsonAsync("/api/preview/strategy", new JsonObject { ["instrument"] = instrument, ["timeframe"] = timeframe, ["strategy"] = strategy }, Json);
    }

    private static async Task<StrategyPreviewResponse> OkStrategy(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StrategyPreviewResponse>(Json))!;
    }

    // The signal of H0001 saved as funding-high.
    private static async Task<Guid> SaveSignal(BenchTestApplication api)
    {
        HttpResponseMessage created = await api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = "funding-high", ["graph"] = Signal(), ["layout"] = new JsonObject() }, Json);
        return (await created.Content.ReadFromJsonAsync<ProfitFox.Presentation.Contracts.Signals.SignalResponse>(Json))!.Id;
    }

    // A run of H0001 with the outcomes of its events on the same fake history.
    private static async Task<ExecuteRunResult> RunWithOutcomes(MarketDataServer market)
    {
        await using ServiceProvider services = BenchServices.Start(market);
        var command = new ExecuteRunCommand("H0001", Version: null, Period.Exploration, Parity: false, new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        return await services.GetRequiredService<ISender>().Send(command, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> Preview(BenchTestApplication api, string instrument, string timeframe, JsonObject signal)
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

    // A parity run of H0001 on the same fake history.
    private static async Task<ExecuteRunResult> RunParity(MarketDataServer market)
    {
        await using ServiceProvider services = BenchServices.Start(market);
        var command = new ExecuteRunCommand("H0001", Version: null, Period.Exploration, Parity: true, new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        return await services.GetRequiredService<ISender>().Send(command, CancellationToken.None);
    }

    private static JsonObject Signal()
    {
        return JsonNode.Parse(H0001File())!["signal"]!.AsObject().DeepClone().AsObject();
    }

    private static string H0001File()
    {
        return BenchRepository.H0001Text();
    }
}
