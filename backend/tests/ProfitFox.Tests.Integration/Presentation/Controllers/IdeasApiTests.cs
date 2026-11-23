using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Filters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Market;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-ideas-and-market, tasks 11.1–11.4 and 11.6: the inclusion of ideas and the labels of signals, the lists of
///     ideas and of the market, the page and the chart of a pair, and the saved filters through the API.
/// </summary>
[NonParallelizable]
public sealed class IdeasApiTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 14, 32, 5, TimeSpan.Zero);
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market, time: new FixedTime(Now));
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    /// <summary>
    ///     Task 11.1: spec strategy-ideas «Включение идей у стратегии».
    /// </summary>
    [Test]
    public async Task Ideas_are_turned_on_on_two_timeframes_and_the_list_of_strategies_gives_them()
    {
        Guid signal = await CreateSignal("funding-high", ReferenceSignals.H0001());
        await CreateStrategy("funding-short", StrategiesTests.Strategy(signal), "idea");
        await CreateStrategy("ladder", JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ladder.json")))!.AsObject(), "bot");

        HttpResponseMessage on = await _api.Http.PutAsJsonAsync("/api/strategies/funding-short/ideas", new StrategyIdeasRequest(true, ["1h", "4h"]), Json);
        HttpResponseMessage empty = await _api.Http.PutAsJsonAsync("/api/strategies/funding-short/ideas", new StrategyIdeasRequest(true, []), Json);
        HttpResponseMessage bot = await _api.Http.PutAsJsonAsync("/api/strategies/ladder/ideas", new StrategyIdeasRequest(true, ["1h"]), Json);
        HttpResponseMessage missing = await _api.Http.PutAsJsonAsync("/api/strategies/nothing/ideas", new StrategyIdeasRequest(true, ["1h"]), Json);

        Assert.That(on.StatusCode, Is.EqualTo(HttpStatusCode.OK), await on.Content.ReadAsStringAsync());
        Assert.That(await Errors(empty), Is.EqualTo(new[] { ("NoTimeframes", (string?)"timeframes") }));
        Assert.That(await Errors(bot), Is.EqualTo(new[] { ("BotGivesNoIdeas", (string?)"strategy") }));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        StrategyListItem[] list = (await _api.Http.GetFromJsonAsync<StrategyListItem[]>("/api/strategies", Json))!;
        StrategyIdeasResponse ideas = list.Single(item => item.Name == "funding-short").Ideas!;
        Assert.That((ideas.Enabled, ideas.Timeframes), Is.EqualTo((true, new[] { "1h", "4h" })).Using<(bool, IReadOnlyList<string>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)), "the refused request changed nothing");
        Assert.That(list.Single(item => item.Name == "ladder").Ideas?.Enabled ?? false, Is.False);
    }

    /// <summary>
    ///     Task 11.1: spec idea-labels «Метка сигнала».
    /// </summary>
    [Test]
    public async Task A_label_is_turned_on_and_its_errors_come_at_the_field_or_the_node()
    {
        await CreateSignal("btc-drop-4h", ReferenceSignals.H0001());
        await CreateSignal("funding-spike", ReferenceSignals.H0001());
        await CreateSignal("doge-drop", PairBelow("DOGEUSDT"));

        HttpResponseMessage on = await _api.Http.PutAsJsonAsync("/api/signals/btc-drop-4h/label", new SignalLabelRequest(true, "↓ BTC", "4h"), Json);
        HttpResponseMessage empty = await _api.Http.PutAsJsonAsync("/api/signals/funding-spike/label", new SignalLabelRequest(true, "", "4h"), Json);
        HttpResponseMessage taken = await _api.Http.PutAsJsonAsync("/api/signals/funding-spike/label", new SignalLabelRequest(true, "↓ BTC", "1h"), Json);
        HttpResponseMessage unknown = await _api.Http.PutAsJsonAsync("/api/signals/doge-drop/label", new SignalLabelRequest(true, "↓ DOGE", "4h"), Json);

        Assert.That(on.StatusCode, Is.EqualTo(HttpStatusCode.OK), await on.Content.ReadAsStringAsync());
        Assert.That(await Errors(empty), Is.EqualTo(new[] { ("InvalidText", (string?)"text") }));
        Assert.That(await Errors(taken), Is.EqualTo(new[] { ("TakenText", (string?)"text") }));
        ErrorResponse pair = (await unknown.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That((unknown.StatusCode, pair.Errors.Single().Node), Is.EqualTo((HttpStatusCode.UnprocessableEntity, (string?)"doge")));
        SignalListItem[] list = (await _api.Http.GetFromJsonAsync<SignalListItem[]>("/api/signals", Json))!;
        Assert.That(list.Single(item => item.Name == "btc-drop-4h").Label, Is.EqualTo(new SignalLabelResponse(true, "↓ BTC", "4h")));
        Assert.That(list.Single(item => item.Name == "funding-spike").Label, Is.Null);
    }

    /// <summary>
    ///     Task 11.2: spec strategy-ideas «Список идей» and «Сводка журнала».
    /// </summary>
    [Test]
    public async Task The_lists_of_ideas_and_the_summary_of_the_journal()
    {
        Idea early = await Add(IdeaGenerator.Generate(symbol: "BTCUSDT", moment: IdeaGenerator.Noon.AddHours(-4)));
        Idea late = await Add(IdeaGenerator.Generate(symbol: "SOLUSDT", moment: IdeaGenerator.Noon));
        await Add(IdeaGenerator.Generate(name: "price-drop-long", symbol: "ETHUSDT", side: EntrySide.Long, moment: IdeaGenerator.Noon.AddHours(-8)));
        var hour = TimeSpan.FromHours(1);
        var fundingShort = Guid.NewGuid();
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "AUSDT", timeframe: hour), closed: 0.004);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "BUSDT", timeframe: hour), closed: -0.002);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "CUSDT", timeframe: hour), closed: -0.005);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "DUSDT", timeframe: hour), excluded: true);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "EUSDT", timeframe: hour), closed: 0.01, exitDaysAgo: 30);

        IdeaPageResponse open = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas", Json))!;
        IdeaPageResponse shorts = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?strategy=funding-short&side=short", Json))!;
        IdeaPageResponse week = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?state=closed&period=7", Json))!;
        IdeaSummaryPageResponse summary = (await _api.Http.GetFromJsonAsync<IdeaSummaryPageResponse>("/api/ideas/summary?period=7", Json))!;
        HttpResponseMessage secret = await _api.Http.GetAsync("/api/ideas?sort=secret");

        Assert.That((open.Total, open.Page, open.PageSize), Is.EqualTo((3, 1, 50)));
        Assert.That(open.Items.Select(idea => idea.Symbol), Is.EqualTo(new[] { "SOLUSDT", "BTCUSDT", "ETHUSDT" }), "from the newest");
        Assert.That((shorts.Total, shorts.Items.Select(idea => idea.Id)), Is.EqualTo((2, new[] { late.Id, early.Id })).Using<(int, IEnumerable<Guid>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(shorts.Items[0], Has.Property(nameof(IdeaResponse.Side)).EqualTo("short").And.Property(nameof(IdeaResponse.Timeframe)).EqualTo("4h").And.Property(nameof(IdeaResponse.State)).EqualTo("open"));
        Assert.That(week.Items.Select(idea => idea.Symbol), Is.EquivalentTo(new[] { "AUSDT", "BUSDT", "CUSDT" }), "the exit of EUSDT is 30 days back; DUSDT is excluded");
        IdeaSummaryResponse row = summary.Items.Single();
        Assert.That((row.Strategy, row.Version, row.Timeframe, row.Ideas), Is.EqualTo(("funding-short", 4, "1h", 3)));
        Assert.That(row.Wins, Is.EqualTo(1.0 / 3).Within(1e-12));
        Assert.That(row.Mean, Is.EqualTo(-0.001).Within(1e-12));
        Assert.That(await Errors(secret), Is.EqualTo(new[] { ("UnknownField", (string?)"sort") }));
    }

    /// <summary>
    ///     Task 11.2: the state of the live data when nobody ever kept it, and the counts of the ideas.
    /// </summary>
    [Test]
    public async Task The_state_of_the_scanner()
    {
        await Add(IdeaGenerator.Generate());

        ScannerStateResponse idle = (await _api.Http.GetFromJsonAsync<ScannerStateResponse>("/api/ideas/state", Json))!;
        await BenchServices.Get<IScannerLeaseRepository>().TakeAsync("worker-a", TimeSpan.FromMinutes(1), CancellationToken.None);
        await BenchServices.Get<IScannerLeaseRepository>().ReportAsync("worker-a", Now, 310, 831, CancellationToken.None, failed: 521, failure: "answered 400");
        ScannerStateResponse live = (await _api.Http.GetFromJsonAsync<ScannerStateResponse>("/api/ideas/state", Json))!;

        Assert.That((idle.Worker, idle.Live, idle.Open, idle.Closed), Is.EqualTo(((string?)null, false, 1, 0)));
        Assert.That((live.Worker, live.Live, live.Warmed, live.Pairs, live.LiveSince), Is.EqualTo(("worker-a", true, 310, 831, (long?)Now.ToUnixTimeMilliseconds())));
        Assert.That(live.Burst.Keys, Is.SupersetOf(new[] { "1h" }));
        Assert.That((idle.Failed, idle.Failure, live.Failed, live.Failure), Is.EqualTo((0, (string?)null, 521, "answered 400")), "task 18.3");
    }

    /// <summary>
    ///     Task 11.3: spec market-overview «Список пар рынка».
    /// </summary>
    [Test]
    public async Task The_list_of_the_market()
    {
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync(
            [Pair("BTCUSDT", 900_000_000m, 0.01m), Pair("ETHUSDT", 400_000_000m, null), Pair("SOLUSDT", 120_000_000m, -0.02m)],
            CancellationToken.None);
        await Add(IdeaGenerator.Generate(symbol: "SOLUSDT"));
        await Add(IdeaGenerator.Generate(name: "price-drop-long", symbol: "ETHUSDT", side: EntrySide.Long, timeframe: TimeSpan.FromHours(1)));

        MarketPageResponse all = (await _api.Http.GetFromJsonAsync<MarketPageResponse>("/api/market/pairs", Json))!;
        MarketPageResponse withIdeas = (await _api.Http.GetFromJsonAsync<MarketPageResponse>("/api/market/pairs?ideasOnly=true", Json))!;
        MarketPageResponse fourHours = (await _api.Http.GetFromJsonAsync<MarketPageResponse>("/api/market/pairs?strategy=funding-short&timeframe=4h", Json))!;
        MarketPageResponse up = (await _api.Http.GetFromJsonAsync<MarketPageResponse>("/api/market/pairs?sort=change24h&direction=asc", Json))!;

        Assert.That(all.Items.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }), "by the turnover of 24 hours from the largest");
        Assert.That(all.UpdatedAt, Is.EqualTo(Now.ToUnixTimeMilliseconds()));
        Assert.That(all.Items.Single(pair => pair.Symbol == "SOLUSDT").Ideas, Is.EqualTo(new[] { new IdeaBadgeResponse("funding-short", "4h", "short") }));
        Assert.That((withIdeas.Total, withIdeas.Items.Select(pair => pair.Symbol)), Is.EqualTo((2, new[] { "ETHUSDT", "SOLUSDT" })).Using<(int, IEnumerable<string>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(fourHours.Items.Select(pair => pair.Symbol), Is.EqualTo(new[] { "SOLUSDT" }));
        Assert.That(up.Items.Select(pair => pair.Symbol), Is.EqualTo(new[] { "SOLUSDT", "BTCUSDT", "ETHUSDT" }), "an empty value goes last");
    }

    /// <summary>
    ///     Task 11.3: spec market-overview «Страница пары» and «Идеи пары».
    /// </summary>
    [Test]
    public async Task The_page_of_a_pair_and_its_ideas()
    {
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync([Pair("SOLUSDT", 120_000_000m, 0.01m)], CancellationToken.None);
        await Add(IdeaGenerator.Generate(symbol: "SOLUSDT"));
        foreach (int days in new[] { 1, 2, 3 })
        {
            await Add(IdeaGenerator.Generate(symbol: "SOLUSDT", moment: IdeaGenerator.Noon.AddDays(-days)), closed: 0.001);
        }

        await Add(IdeaGenerator.Generate(symbol: "BTCUSDT"));

        MarketPairResponse sol = (await _api.Http.GetFromJsonAsync<MarketPairResponse>("/api/market/pairs/SOLUSDT", Json))!;
        MarketPairResponse eth = (await _api.Http.GetFromJsonAsync<MarketPairResponse>("/api/market/pairs/ETHUSDT", Json))!;
        HttpResponseMessage none = await _api.Http.GetAsync("/api/market/pairs/NOSUCHUSDT");
        IdeaPageResponse ideas = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/market/pairs/SOLUSDT/ideas", Json))!;
        IdeaPageResponse open = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/market/pairs/SOLUSDT/ideas?state=open", Json))!;
        IdeaPageResponse empty = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/market/pairs/ETHUSDT/ideas", Json))!;

        Assert.That((sol.Symbol, sol.Turnover24h, sol.Ideas.Count), Is.EqualTo(("SOLUSDT", (double?)120_000_000, 1)));
        Assert.That((eth.Symbol, eth.Price), Is.EqualTo(("ETHUSDT", (double?)null)), "a listed pair without a snapshot yet");
        Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ideas.Items.Select(idea => idea.State), Is.EqualTo(new[] { "open", "closed", "closed", "closed" }), "from the newest");
        Assert.That(open.Items.Select(idea => idea.State), Is.EqualTo(new[] { "open" }));
        Assert.That(empty.Total, Is.Zero);
    }

    /// <summary>
    ///     Task 11.4: spec market-overview «График пары» and «OI и фандинг под графиком» on the fake REST.
    /// </summary>
    [Test]
    public async Task The_chart_goes_back_a_page_at_a_time_with_the_open_interest_and_the_payments()
    {
        PairChartResponse first = (await _api.Http.GetFromJsonAsync<PairChartResponse>("/api/market/pairs/SOLUSDT/chart?timeframe=1h", Json))!;
        PairChartResponse earlier = (await _api.Http.GetFromJsonAsync<PairChartResponse>($"/api/market/pairs/SOLUSDT/chart?timeframe=1h&before={first.Candles[0].OpenTime}", Json))!;
        PairChartResponse fifteen = (await _api.Http.GetFromJsonAsync<PairChartResponse>("/api/market/pairs/SOLUSDT/chart?timeframe=15m", Json))!;
        HttpResponseMessage minute = await _api.Http.GetAsync("/api/market/pairs/SOLUSDT/chart?timeframe=1m");
        HttpResponseMessage none = await _api.Http.GetAsync("/api/market/pairs/NOSUCHUSDT/chart?timeframe=1h");
        _market.Data.Loading.Add("sol");
        PairChartResponse loading = (await _api.Http.GetFromJsonAsync<PairChartResponse>("/api/market/pairs/SOLUSDT/chart?timeframe=1h", Json))!;

        Assert.That((first.Timeframe, first.Loading, first.Candles.Count), Is.EqualTo(("1h", false, 500)));
        Assert.That(DateTimeOffset.FromUnixTimeMilliseconds(first.Candles[^1].OpenTime), Is.EqualTo(new DateTimeOffset(2026, 11, 22, 13, 0, 0, TimeSpan.Zero)), "the candle closed at 14:00 of the current day");
        Assert.That(earlier.Candles[^1].OpenTime, Is.EqualTo(first.Candles[0].OpenTime - (long)TimeSpan.FromHours(1).TotalMilliseconds), "the page before, without a gap or an overlap");
        Assert.That(first.OpenInterest.Count, Is.EqualTo(500));
        Assert.That(first.Funding.Select(point => point.Time), Is.All.InRange(first.Candles[0].OpenTime, Now.ToUnixTimeMilliseconds()));
        Assert.That(first.Funding, Has.Count.InRange(62, 63), "a payment every 8 hours over 500 hours");
        Assert.That(fifteen.Candles[1].OpenTime - fifteen.Candles[0].OpenTime, Is.EqualTo((long)TimeSpan.FromMinutes(15).TotalMilliseconds));
        Assert.That(await Errors(minute), Is.EqualTo(new[] { ("InvalidTimeframe", (string?)"timeframe") }));
        Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((loading.Loading, loading.Candles), Is.EqualTo((true, Array.Empty<ChartCandle>())).Using<(bool, IReadOnlyList<ChartCandle>)>((a, b) => a.Item1 == b.Item1 && a.Item2.Count == b.Item2.Count));
    }

    /// <summary>
    ///     Task 11.5: spec market-overview «Живая свеча» end to end — the candle of the hour from the minutes read and the one
    ///     that goes on, as a stream; the subscription of the feed lives while the page listens.
    /// </summary>
    [Test]
    public async Task The_live_candle_comes_as_a_stream_while_the_page_listens()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/market/pairs/SOLUSDT/live?timeframe=1h");
        using HttpResponseMessage response = await _api.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        await using Stream body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);
        await Until(() => _market.Data.Minutes.Readers == 1);
        // Task 19.5: right after subscribing the page gets the candle of the minutes read, before a minute of the feed.
        LiveCandleResponse seed = await NextCandle(reader);
        Assert.That((seed.Volume, seed.Closed), Is.EqualTo((32 * 12.5, false)), "the 32 minutes read from the REST");

        CandleUpdate forming = FakeCandleFeed.Minute("sol", new DateTimeOffset(2026, 11, 22, 14, 32, 0, TimeSpan.Zero));
        forming.High = "102";
        forming.Close = "101.5";
        forming.Closed = false;
        _market.Data.Minutes.PushForming(forming);
        LiveCandleResponse candle = await NextCandle(reader);

        Assert.That(DateTimeOffset.FromUnixTimeMilliseconds(candle.OpenTime), Is.EqualTo(new DateTimeOffset(2026, 11, 22, 14, 0, 0, TimeSpan.Zero)));
        Assert.That((candle.Open, candle.High, candle.Low, candle.Close, candle.Closed), Is.EqualTo((100.5, 102.0, 100.0, 101.5, false)));
        Assert.That(candle.Volume, Is.EqualTo(33 * 12.5), "32 minutes read from the REST and the one that goes on");
        Assert.That(_market.Data.RestRequests, Has.Member("candles sol 1m"));

        request.Dispose();
        response.Dispose();
        await Until(() => _market.Data.Minutes.Readers == 0);
        Assert.That(_market.Data.Minutes.Readers, Is.Zero, "the last page went: no forming candles come any more");
    }

    /// <summary>
    ///     Task 18.22: the chart asks the ideas of the pair that touch its window, whatever the table chose; the table opens
    ///     on the page of the idea it was led to.
    /// </summary>
    [Test]
    public async Task The_ideas_of_the_window_of_the_chart_and_the_page_of_an_idea()
    {
        DateTimeOffset day = Now.AddDays(-1);
        List<Guid> ids = [];
        for (int hour = 0; hour < 60; hour++)
        {
            Idea idea = IdeaGenerator.Generate(symbol: "SOLUSDT", moment: day.AddHours(-hour), after: TimeSpan.FromHours(1));
            await Add(idea);
            ids.Add(idea.Id);
        }

        long from = day.AddHours(-10).ToUnixTimeMilliseconds();
        long to = day.AddHours(-5).ToUnixTimeMilliseconds();
        IdeaPageResponse window = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>($"/api/market/pairs/SOLUSDT/ideas?from={from}&to={to}&pageSize=200", Json))!;
        IdeaPageResponse led = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>($"/api/market/pairs/SOLUSDT/ideas?idea={ids[55]}", Json))!;
        IdeaPageResponse unknown = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>($"/api/market/pairs/SOLUSDT/ideas?idea={Guid.NewGuid()}&page=1", Json))!;

        Assert.That(window.Items.Select(item => item.Id), Is.EquivalentTo(ids.Skip(5).Take(7)), "born by 5 hours back and not over before 10 hours back");
        Assert.That((led.Page, led.Items.Any(item => item.Id == ids[55])), Is.EqualTo((2, true)), "the 56th from the newest is on the second page of 50");
        Assert.That(unknown.Page, Is.EqualTo(1));
    }

    /// <summary>
    ///     Task 18.11 (б): the live candle of a pair outside the set is 404 before any stream, so the page does not connect
    ///     again for ever.
    /// </summary>
    [Test]
    public async Task The_live_candle_of_a_pair_outside_the_set_is_not_found()
    {
        using HttpResponseMessage response = await _api.Http.GetAsync("/api/market/pairs/NOSUCHUSDT/live?timeframe=1h", HttpCompletionOption.ResponseHeadersRead);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("text/event-stream"));
    }

    /// <summary>
    ///     Task 11.6: spec saved-filters — the filters through the API, the errors at the name or the number of the condition.
    /// </summary>
    [Test]
    public async Task Saved_filters_and_their_errors_at_their_places()
    {
        FilterConditionDto[] high = [new FilterConditionDto("fundingRate", "ge", "0.03"), new FilterConditionDto("turnover24h", "gt", "100")];
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/filters", new SaveFilterRequest("Высокий фандинг", "market", high), Json);
        HttpResponseMessage taken = await _api.Http.PostAsJsonAsync("/api/filters", new SaveFilterRequest("Высокий фандинг", "market", high), Json);
        HttpResponseMessage none = await _api.Http.PostAsJsonAsync("/api/filters", new SaveFilterRequest("Пусто", "market", []), Json);
        HttpResponseMessage side = await _api.Http.PostAsJsonAsync(
            "/api/filters",
            new SaveFilterRequest("Шорты", "ideas", [new FilterConditionDto("side", "eq", "short"), new FilterConditionDto("side", "gt", "short")]),
            Json);
        HttpResponseMessage word = await _api.Http.PostAsJsonAsync("/api/filters/count", new FilterCountRequest([new FilterConditionDto("fundingRate", "ge", "abc")]), Json);

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        FilterResponse filter = (await created.Content.ReadFromJsonAsync<FilterResponse>(Json))!;
        Assert.That((filter.Name, filter.Place, filter.Conditions), Is.EqualTo(("Высокий фандинг", "market", high)).Using<(string, string, IReadOnlyList<FilterConditionDto>)>((a, b) => a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3.SequenceEqual(b.Item3)));
        Assert.That(await Errors(taken), Is.EqualTo(new[] { ("TakenName", (string?)"name") }));
        Assert.That(await Errors(none), Is.EqualTo(new[] { ("NoConditions", (string?)"conditions") }));
        Assert.That(await Conditions(side), Is.EqualTo(new int?[] { 1 }), "the second condition: «сторона > шорт»");
        Assert.That(await Conditions(word), Is.EqualTo(new int?[] { 0 }));

        HttpResponseMessage renamed = await _api.Http.PutAsJsonAsync($"/api/filters/{filter.Id}", new SaveFilterRequest("Фандинг", "both", high), Json);
        HttpResponseMessage gone = await _api.Http.PutAsJsonAsync($"/api/filters/{Guid.NewGuid()}", new SaveFilterRequest("Фандинг", "both", high), Json);
        FilterResponse[] list = (await _api.Http.GetFromJsonAsync<FilterResponse[]>("/api/filters", Json))!;
        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(list.Select(item => (item.Name, item.Place)), Is.EqualTo(new[] { ("Фандинг", "both") }));
        Assert.That((await _api.Http.DeleteAsync($"/api/filters/{filter.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await _api.Http.DeleteAsync($"/api/filters/{filter.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.GetFromJsonAsync<FilterResponse[]>("/api/filters", Json))!, Is.Empty);
    }

    /// <summary>
    ///     Task 11.6: spec saved-filters «Фильтр не этого места», «Перечень полей» and «Счётчик подходящих строк».
    /// </summary>
    [Test]
    public async Task A_filter_of_another_place_the_fields_and_the_count()
    {
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync(
            [Pair("BTCUSDT", 900_000_000m, 0.01m, funding: 0.0004m), Pair("ETHUSDT", 50_000_000m, 0.01m, funding: 0.0004m), Pair("SOLUSDT", 120_000_000m, 0.01m)],
            CancellationToken.None);
        await Add(IdeaGenerator.Generate(symbol: "BTCUSDT"));
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/filters", new SaveFilterRequest("Высокий фандинг", "market", [new FilterConditionDto("fundingRate", "ge", "0.03")]), Json);
        FilterResponse market = (await created.Content.ReadFromJsonAsync<FilterResponse>(Json))!;

        HttpResponseMessage ideas = await _api.Http.GetAsync($"/api/ideas?filter={market.Id}");
        MarketPageResponse pairs = (await _api.Http.GetFromJsonAsync<MarketPageResponse>($"/api/market/pairs?filter={market.Id}&search=USD", Json))!;
        MarketPageResponse gone = (await _api.Http.GetFromJsonAsync<MarketPageResponse>($"/api/market/pairs?filter={Guid.NewGuid()}", Json))!;
        FilterFieldResponse[] fields = (await _api.Http.GetFromJsonAsync<FilterFieldResponse[]>("/api/filters/fields", Json))!;
        HttpResponseMessage counted = await _api.Http.PostAsJsonAsync(
            "/api/filters/count",
            new FilterCountRequest([new FilterConditionDto("fundingRate", "ge", "0.03"), new FilterConditionDto("turnover24h", "gt", "100")]),
            Json);

        Assert.That(await Errors(ideas), Is.EqualTo(new[] { ("FilterOfAnotherPlace", (string?)"filter") }));
        Assert.That(pairs.Items.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
        Assert.That((gone.FilterGone, gone.Total), Is.EqualTo((true, 3)), "a filter deleted elsewhere is dropped and said so");
        FilterFieldResponse sideField = fields.Single(field => field.Name == "side");
        FilterFieldResponse funding = fields.Single(field => field.Name == "fundingRate");
        Assert.That(sideField.Values, Is.EqualTo(new[] { "long", "short" }));
        Assert.That((funding.Type, funding.Unit, funding.Place), Is.EqualTo(("number", (string?)"%", "market")));
        FilterCountResponse count = (await counted.Content.ReadFromJsonAsync<FilterCountResponse>(Json))!;
        Assert.That(count, Is.EqualTo(new FilterCountResponse(1, 3, 1, 1)));
    }

    private static MarketPair Pair(string symbol, decimal turnover, decimal? change24h, decimal funding = 0.0001m)
    {
        return new MarketPair(symbol, 100, 0, 0, change24h, turnover, 1000, 100_000, funding, null, Now);
    }

    private static async Task<Idea> Add(Idea idea, double? closed = null, bool excluded = false, int exitDaysAgo = 1)
    {
        IIdeaRepository ideas = BenchServices.Get<IIdeaRepository>();
        await ideas.AddAsync(idea, CancellationToken.None);
        if (closed is { } result)
        {
            idea.State = IdeaState.Closed;
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-exitDaysAgo);
            idea.ExitReason = ExitReason.Time;
            idea.Result = result;
            idea.ResultUsdt = result * 100;
            await ideas.SaveAsync(idea, CancellationToken.None);
        }
        else if (excluded)
        {
            idea.State = IdeaState.Excluded;
            idea.Excluded = "нет данных";
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-1);
            await ideas.SaveAsync(idea, CancellationToken.None);
        }

        return idea;
    }

    // A signal: the close of the named pair first below 95000.
    private static JsonObject PairBelow(string symbol)
    {
        return JsonNode.Parse(
            $$"""
              {
                "formatVersion": 1,
                "outputs": [{ "name": "entry", "type": "event" }],
                "nodes": [
                  { "id": "doge", "kind": "pair-candle", "settings": { "symbol": "{{symbol}}" } },
                  { "id": "low", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": 95000 } },
                  { "id": "first", "kind": "first-time" },
                  { "id": "out", "kind": "outputs" }
                ],
                "wires": [
                  { "from": "doge.close", "to": "low.a" },
                  { "from": "low.result", "to": "first.value" },
                  { "from": "first.event", "to": "out.entry" }
                ]
              }
              """)!.AsObject();
    }

    private static async Task<(string Code, string? Field)[]> Errors(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Select(error => (error.Code, error.Field))];
    }

    private static async Task<int?[]> Conditions(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        ErrorResponse errors = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => error.Field), Is.All.EqualTo("conditions"));
        return [.. errors.Errors.Select(error => error.Condition)];
    }

    private static async Task<LiveCandleResponse> NextCandle(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string? name = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && name == "candle")
            {
                return JsonSerializer.Deserialize<LiveCandleResponse>(line["data: ".Length..], Json)!;
            }
        }

        throw new InvalidOperationException("the stream ended without a candle");
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private async Task<Guid> CreateSignal(string name, JsonObject graph)
    {
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = new JsonObject() }, Json);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<SignalResponse>(Json))!.Id;
    }

    private async Task CreateStrategy(string name, JsonObject graph, string kind)
    {
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = new JsonObject(), ["kind"] = kind }, Json);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
    }
}
