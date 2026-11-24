using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Repositories;
using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Integration.Worker;

using static ProfitFox.Tests.Integration.Presentation.Backtests;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-strategy-backtest, tasks 7.1, 7.4 and 7.5: backtests through the API with a Worker of the test process
///     over four weeks of three pairs — spec strategy-backtest «Прогон версии стратегии», «Набор на остатке истории»,
///     «Подсказка, а не вердикт», «Предупреждение о выживших парах»; spec bench-run-queue «Постановка прогона стратегии»;
///     spec strategy-constructor «Последний прогон стратегии».
/// </summary>
[NonParallelizable]
public sealed class BacktestsTests
{
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;
    private WorkerHost? _worker;

    private HttpClient Http => _api.Http;

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market);
    }

    [TearDown]
    public async Task Stop()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        await _api.DisposeAsync();
        await _market.DisposeAsync();
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_backtest_without_a_version_is_of_the_latest_and_is_queued_with_an_id()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        await ReplaceStrategy(Http, "funding-short", FundingShort(after: "8h"));
        await ReplaceStrategy(Http, "funding-short", FundingShort(after: "12h"));

        HttpResponseMessage created = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        BacktestResponse backtest = (await created.Content.ReadFromJsonAsync<BacktestResponse>(Json))!;
        Assert.That(created.Headers.Location!.ToString(), Does.EndWith($"/api/backtests/{backtest.Id}"));
        Assert.That((backtest.State, backtest.Strategy.Name, backtest.Strategy.Version, backtest.Strategy.Kind), Is.EqualTo(("queued", "funding-short", 3, "idea")));
        Assert.That((await Http.GetFromJsonAsync<BacktestResponse[]>("/api/backtests", Json))!.Select(item => item.Id), Is.EqualTo(new[] { backtest.Id }));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 2.1: spec strategy-backtest «Прогон на 15m» and «Две ошибки сразу».
    /// </summary>
    [Test]
    public async Task A_backtest_on_15m_and_1h_is_queued_with_two_sets()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");

        BacktestResponse backtest = await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["timeframes"] = new JsonArray("15m", "1h"), ["end"] = End });

        Assert.That(backtest.Sets.Select(set => set.Timeframe), Is.EqualTo(new[] { "15m", "1h" }));
        Assert.That((await Read(Http, backtest.Id)).Settings.Timeframes, Is.EqualTo(new[] { "15m", "1h" }));
    }

    [Test]
    public async Task A_timeframe_of_2m_and_an_unknown_pair_are_refused_together()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");

        HttpResponseMessage refused = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["timeframes"] = new JsonArray("2m"), ["pairs"] = new JsonArray("FOOUSDT") });

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => error.Code), Is.EqualTo(new[] { "InvalidTimeframe", "UnknownInstrument" }));
        Assert.That(await Scalar<int>("SELECT count(*) FROM backtests"), Is.Zero);
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 4.3: spec market-history «Неизвестная пара».
    /// </summary>
    [Test]
    public async Task A_strategy_that_names_a_pair_not_listed_is_not_queued_with_an_error_of_the_node()
    {
        await SaveStrategy(Http, "funding-short", FundingShortWithPair("NOSUCHUSDT"), "idea");

        HttpResponseMessage refused = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End });

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await refused.Content.ReadAsStringAsync());
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => (error.Code, error.Node, error.Setting)), Is.EqualTo(new[] { ("UnknownPair", (string?)"btc", (string?)"symbol") }));
        Assert.That(await Scalar<int>("SELECT count(*) FROM backtests"), Is.Zero);
    }

    [Test]
    public async Task A_version_saved_after_the_backtest_does_not_change_it_nor_does_deleting_the_strategy()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        BacktestResponse queued = await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End });

        await ReplaceStrategy(Http, "funding-short", FundingShort(after: "8h"));
        await Http.DeleteAsync("/api/strategies/funding-short");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, queued.Id);

        Assert.That((done.State, done.Strategy.Name, done.Strategy.Version), Is.EqualTo(("done", "funding-short", 1)));
        Assert.That(await Scalar<string>("SELECT n -> 'inputs' ->> 'after' FROM backtests, jsonb_array_elements(graph -> 'nodes') n WHERE id = @Id AND n ->> 'kind' = 'close-after'", new { queued.Id }), Is.EqualTo("4h"));
        Assert.That(done.Sets.Single().Numbers!.Trades, Is.GreaterThan(0));
    }

    [Test]
    public async Task A_version_by_its_number_and_one_that_is_not_there()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        await ReplaceStrategy(Http, "funding-short", FundingShort(after: "8h"));

        BacktestResponse first = await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["version"] = 1, ["end"] = End });
        HttpResponseMessage missing = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["version"] = 7 });
        HttpResponseMessage unknown = await Post(Http, new JsonObject { ["strategy"] = "price-drop-long" });

        Assert.That(first.Strategy.Version, Is.EqualTo(1));
        Assert.That((missing.StatusCode, unknown.StatusCode), Is.EqualTo((HttpStatusCode.NotFound, HttpStatusCode.NotFound)));
        Assert.That(await Scalar<int>("SELECT count(*) FROM backtests"), Is.EqualTo(1), "a refusal queues nothing");
    }

    [Test]
    public async Task A_version_whose_graph_has_an_error_is_refused_with_it()
    {
        JsonObject graph = FundingShort();
        graph["nodes"]!.AsArray().Single(node => (string)node!["id"]! == "enter")!.AsObject().Remove("settings");
        await BenchServices.Get<IStrategyRepository>().CreateAsync(
            ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies.StrategyDocumentGenerator.Generate(
                Guid.Empty,
                graph: ProfitFox.Domain.Services.Graph.StrategyGraph.ReadForm(System.Text.Json.JsonDocument.Parse(graph.ToJsonString()).RootElement).Value,
                kind: ProfitFox.Domain.ValueObjects.Strategies.Enums.StrategyKind.Idea),
            CancellationToken.None);

        HttpResponseMessage refused = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["timeframes"] = new JsonArray("2m") });

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => (error.Code, error.Node, error.Setting)), Is.EqualTo(new[]
        {
            ("MissingSetting", (string?)"enter", (string?)"side"), ("InvalidTimeframe", null, null)
        }), "the graph and the settings at once");
        Assert.That(await Scalar<int>("SELECT count(*) FROM backtests"), Is.Zero);
    }

    /// <summary>
    ///     Task 12.9: values written as null are an error of the field, not a failure of the API.
    /// </summary>
    [Test]
    public async Task Values_of_null_are_refused_with_an_error_of_the_field()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");

        HttpResponseMessage list = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["values"] = new JsonObject { ["threshold"] = null } });
        HttpResponseMessage item = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["values"] = new JsonObject { ["threshold"] = new JsonArray("0.001", null) } });

        foreach (HttpResponseMessage refused in new[] { list, item })
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await refused.Content.ReadAsStringAsync());
            ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
            Assert.That(errors.Errors.Select(error => (error.Code, error.Field, error.Parameter)), Is.EqualTo(new[] { ("MissingValue", (string?)"values", (string?)"threshold") }));
        }

        Assert.That(await Scalar<int>("SELECT count(*) FROM backtests"), Is.Zero);
    }

    [Test]
    public async Task The_settings_come_back_with_the_backtest()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");

        BacktestResponse backtest = await Queued(Http, new JsonObject
        {
            ["strategy"] = "funding-short",
            ["values"] = new JsonObject { ["threshold"] = new JsonArray("0.0003", "0.0005", "0.001") },
            ["timeframes"] = new JsonArray("1h", "4h"),
            ["end"] = End,
            ["pairs"] = new JsonArray("BTCUSDT", "ETHUSDT"),
            ["takerFee"] = 0.0006
        });

        BacktestResponse read = await Read(Http, backtest.Id);
        Assert.That(read.Settings.Values.Single().Values, Is.EqualTo(new[] { "0.0003", "0.0005", "0.001" }));
        Assert.That((read.Settings.Start, read.Settings.End, read.Settings.TakerFee, read.Settings.MakerFee), Is.EqualTo(("2023-01-01", End, 0.0006, 0.0002)));
        Assert.That(read.Settings.Pairs, Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
        Assert.That(read.Sets.Select(set => (set.Number, set.Timeframe, set.Values.Single().Value)), Is.EqualTo(new[]
        {
            (1, "1h", "0.0003"), (2, "4h", "0.0003"), (3, "1h", "0.0005"), (4, "4h", "0.0005"), (5, "1h", "0.001"), (6, "4h", "0.001")
        }));
        Assert.That(read.Sets.Select(set => set.Numbers), Has.All.Null, "a waiting backtest has no numbers");
        Assert.That(read.SetCount, Is.EqualTo(6));
    }

    [Test]
    public async Task A_backtest_asked_again_with_the_same_settings_is_queued_and_computed_again_with_the_warning()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        _worker = await WorkerHost.Start(_market, "w-1");
        var request = new JsonObject { ["strategy"] = "funding-short", ["end"] = End };

        BacktestResponse first = await Ended(Http, (await Queued(Http, request)).Id);
        BacktestResponse second = await Ended(Http, (await Queued(Http, request.DeepClone().AsObject())).Id);

        Assert.That((first.State, second.State), Is.EqualTo(("done", "done")), "a backtest is a free test: nothing refuses the next one");
        Assert.That(second.Sets.Single().Numbers, Is.EqualTo(first.Sets.Single().Numbers).Using<SetNumbersResponse>(Same), "the same set on the same data");
        Assert.That(second.DataHash, Is.EqualTo(first.DataHash));
        Assert.That(first.Warning, Does.Contain("снятые с торгов"));
        Assert.That((await Http.GetFromJsonAsync<BacktestResponse[]>("/api/backtests?active=false", Json))!.Select(item => item.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
        Assert.That((first.EngineVersion, first.ComputeSeconds is >= 0, first.ReadSeconds is > 0), Is.EqualTo(((int?)ProfitFox.Domain.ValueObjects.Runs.EngineVersion.Number, true, true)));
    }

    [Test]
    public async Task A_set_and_its_pairs()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, (await Queued(Http, new JsonObject
        {
            ["strategy"] = "funding-short",
            ["values"] = new JsonObject { ["threshold"] = new JsonArray("0.0005", "0.001") },
            ["end"] = End
        })).Id);

        BacktestSetPairsResponse set = (await Http.GetFromJsonAsync<BacktestSetPairsResponse>($"/api/backtests/{done.Id}/sets/1", Json))!;
        BacktestSetPairsResponse none = (await Http.GetFromJsonAsync<BacktestSetPairsResponse>($"/api/backtests/{done.Id}/sets/2", Json))!;

        Assert.That(set.Pairs.Select(pair => pair.Pair), Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }));
        Assert.That(set.Pairs.Sum(pair => pair.Numbers.Trades), Is.EqualTo(set.Set.Numbers!.Trades));
        Assert.That(set.Pairs.Select(pair => pair.Numbers.Amount), Is.Ordered.Descending);
        Assert.That(set.Set.Numbers.Difference, Is.Not.Null, "a category of ideas has its difference from random entries");
        Assert.That((none.Set.Numbers!.Trades, none.Set.Numbers.Mean, none.Pairs.Count), Is.EqualTo((0, (double?)null, 0)), "a threshold of 0.001 never comes");
        Assert.That((await Http.GetAsync($"/api/backtests/{done.Id}/sets/3")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_set_on_the_rest_of_history()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, (await Queued(Http, new JsonObject
        {
            ["strategy"] = "funding-short",
            ["values"] = new JsonObject { ["threshold"] = new JsonArray("0.0005", "0.001") },
            ["end"] = "2026-01-19",
            ["pairs"] = new JsonArray("BTCUSDT", "ETHUSDT")
        })).Id);

        HttpResponseMessage created = await Http.PostAsync($"/api/backtests/{done.Id}/sets/2/rest", content: null);

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        BacktestResponse rest = (await created.Content.ReadFromJsonAsync<BacktestResponse>(Json))!;
        string today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.That((rest.Settings.Start, rest.Settings.End, rest.Source!.Backtest, rest.Source.Set), Is.EqualTo(("2026-01-19", today, done.Id, 2)));
        Assert.That(rest.Sets.Single().Values.Single(), Is.EqualTo(new ParameterValueResponse("threshold", "0.001")));
        Assert.That((rest.Sets.Single().Timeframe, rest.Settings.Pairs, rest.Strategy.Version), Is.EqualTo(("4h", (IReadOnlyList<string>?)new[] { "BTCUSDT", "ETHUSDT" }, 1)).Using<(string, IReadOnlyList<string>?, int)>(
            (a, b) => a.Item1 == b.Item1 && a.Item2!.SequenceEqual(b.Item2!) && a.Item3 == b.Item3));
        Assert.That((await Ended(Http, rest.Id)).State, Is.EqualTo("done"));
    }

    [Test]
    public async Task A_backtest_up_to_today_has_no_rest()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, (await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["pairs"] = new JsonArray("BTCUSDT") })).Id);

        HttpResponseMessage refused = await Http.PostAsync($"/api/backtests/{done.Id}/sets/1/rest", content: null);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Single().Code, Is.EqualTo("NoRest"));
        Assert.That((await Http.PostAsync("/api/backtests/none/sets/1/rest", content: null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_negative_difference_stops_nothing()
    {
        // The short pays the costs on a flat price, so the orders do no better than random entries.
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, (await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End })).Id);

        HttpResponseMessage next = await Post(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End });

        Assert.That(done.State, Is.EqualTo("done"));
        Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task The_list_of_strategies_gives_the_last_backtest_of_each()
    {
        await SaveStrategy(Http, "funding-short", FundingShort(), "idea");
        await SaveStrategy(Http, "ladder", Ladder(), "bot");
        _worker = await WorkerHost.Start(_market, "w-1");
        BacktestResponse done = await Ended(Http, (await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End })).Id);

        StrategyListItem[] ended = (await Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!;
        await _worker.DisposeAsync();
        _worker = null;
        BacktestResponse waiting = await Queued(Http, new JsonObject { ["strategy"] = "funding-short", ["end"] = End });
        StrategyListItem[] queued = (await Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!;

        Assert.That(ended.Single(item => item.Name == "funding-short").LastRun, Is.EqualTo(new StrategyLastRunResponse(done.Id, "done", 1)));
        Assert.That(queued.Single(item => item.Name == "funding-short").LastRun, Is.EqualTo(new StrategyLastRunResponse(waiting.Id, "queued", 1)), "a waiting backtest comes before an ended one");
        Assert.That(queued.Single(item => item.Name == "ladder").LastRun, Is.Null, "a strategy never backtested has none");
    }

    private static bool Same(SetNumbersResponse a, SetNumbersResponse b)
    {
        return a with { Excluded = b.Excluded, Difference = b.Difference } == b && a.Excluded == b.Excluded && a.Difference == b.Difference;
    }
}
