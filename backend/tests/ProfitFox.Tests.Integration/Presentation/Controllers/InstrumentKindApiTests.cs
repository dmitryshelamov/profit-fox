using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Filters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Market;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-instrument-kind-filter, tasks 4.1 and 4.2: the choice of the kind of a pair in the list of the market, the
///     lists of ideas, the summary of the journal and the counts by strategy, and the kind of a pair in its row and its page
///     (spec market-overview «Список пар рынка», «Страница пары»; spec strategy-ideas «Список идей»).
/// </summary>
[NonParallelizable]
public sealed class InstrumentKindApiTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 25, 14, 32, 5, TimeSpan.Zero);

    // The catalog of the fake service with a contract on a share: TSLAUSDT is a stock, the others have no type.
    private static readonly string Catalog = MarketDataServer.Catalog.Replace(
        "\n]",
        """
        ,
          { "id": "tsla", "listing": { "category": "Linear", "symbol": "TSLAUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual", "symbolType": "Stock" }, "delistedAt": null }
        ]
        """.TrimEnd());

    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _market.Data.Catalog = Catalog;
        _api = new BenchTestApplication(_market, time: new FixedTime(Now));
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    // Scenario «Только акции».
    [Test]
    public async Task Only_stocks_gives_the_pairs_of_the_kind_and_counts_only_them()
    {
        MarketPair[] crypto = [.. Enumerable.Range(0, 516).Select(index => Pair($"C{index:D3}USDT", InstrumentKind.Crypto, turnover: 1_000 + index))];
        MarketPair[] stocks = [.. Enumerable.Range(0, 318).Select(index => Pair($"S{index:D3}USDT", InstrumentKind.Stock, turnover: 1_000 + index))];
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync([.. crypto, .. stocks], CancellationToken.None);

        MarketPageResponse all = await Market("");
        MarketPageResponse onlyStocks = await Market("?kind=stock&pageSize=200");
        MarketPageResponse onlyCrypto = await Market("?kind=crypto");

        Assert.That(all.Total, Is.EqualTo(834));
        Assert.That(onlyStocks.Total, Is.EqualTo(318));
        Assert.That(onlyStocks.Items.Select(pair => pair.Kind), Is.All.EqualTo("stock"));
        Assert.That(onlyStocks.Items.Select(pair => pair.Symbol), Is.All.StartsWith("S"));
        Assert.That(onlyCrypto.Total, Is.EqualTo(516));
        Assert.That(onlyCrypto.Items.Select(pair => pair.Kind), Is.All.EqualTo("crypto"));
    }

    // Scenario «Вид и сохранённый фильтр».
    [Test]
    public async Task The_kind_and_a_saved_filter_choose_together()
    {
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync(
            [
                Pair("BTCUSDT", InstrumentKind.Crypto, funding: 0.0004m), Pair("ETHUSDT", InstrumentKind.Crypto),
                Pair("TSLAUSDT", InstrumentKind.Stock, funding: 0.0004m), Pair("NVDAUSDT", InstrumentKind.Stock)
            ],
            CancellationToken.None);
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/filters", new SaveFilterRequest("Высокий фандинг", "market", [new FilterConditionDto("fundingRate", "ge", "0.03")]), Json);
        FilterResponse filter = (await created.Content.ReadFromJsonAsync<FilterResponse>(Json))!;

        MarketPageResponse page = await Market($"?kind=crypto&filter={filter.Id}");

        Assert.That(page.Items.Select(pair => pair.Symbol), Is.EqualTo(new[] { "BTCUSDT" }));
    }

    // Scenario «Неизвестный вид» of «Список пар рынка».
    [Test]
    public async Task An_unknown_kind_of_the_market_is_refused_at_its_field()
    {
        HttpResponseMessage response = await _api.Http.GetAsync("/api/market/pairs?kind=spot");

        Assert.That(await Errors(response), Is.EqualTo(new[] { ("InvalidChoice", (string?)"kind") }));
    }

    // Spec market-overview «Страница пары»: the kind of the pair from the catalog, with a snapshot and before one.
    [Test]
    public async Task The_page_of_a_pair_has_its_kind()
    {
        await BenchServices.Get<IMarketPairRepository>().ReplaceAsync([Pair("TSLAUSDT", InstrumentKind.Stock), Pair("BTCUSDT", InstrumentKind.Crypto)], CancellationToken.None);

        MarketPairResponse tsla = (await _api.Http.GetFromJsonAsync<MarketPairResponse>("/api/market/pairs/TSLAUSDT", Json))!;
        MarketPairResponse btc = (await _api.Http.GetFromJsonAsync<MarketPairResponse>("/api/market/pairs/BTCUSDT", Json))!;
        MarketPairResponse eth = (await _api.Http.GetFromJsonAsync<MarketPairResponse>("/api/market/pairs/ETHUSDT", Json))!;

        Assert.That((tsla.Kind, btc.Kind, eth.Kind), Is.EqualTo(("stock", "crypto", "crypto")));
        Assert.That(eth.Price, Is.Null, "a listed pair without a snapshot yet");
    }

    // Scenario «Только крипта».
    [Test]
    public async Task Only_crypto_gives_the_ideas_of_crypto_pairs()
    {
        await Snapshot();
        Idea btc = await Add(IdeaGenerator.Generate(symbol: "BTCUSDT"));
        Idea tsla = await Add(IdeaGenerator.Generate(symbol: "TSLAUSDT", side: EntrySide.Long));

        IdeaPageResponse crypto = await Ideas("?kind=crypto");
        IdeaPageResponse stocks = await Ideas("?kind=stock");
        IdeaPageResponse all = await Ideas("");

        Assert.That((crypto.Total, crypto.Items.Select(idea => idea.Id)), Is.EqualTo((1, new[] { btc.Id })).Using<(int, IEnumerable<Guid>)>(Same));
        Assert.That(stocks.Items.Select(idea => idea.Id), Is.EqualTo(new[] { tsla.Id }));
        Assert.That(all.Total, Is.EqualTo(2));
    }

    // Scenario «Пары больше нет в наборе».
    [Test]
    public async Task A_closed_idea_of_a_pair_out_of_the_snapshot_is_not_of_any_kind()
    {
        await Snapshot();
        Idea gone = await Add(IdeaGenerator.Generate(symbol: "OLDUSDT"), closed: 0.01);
        Idea btc = await Add(IdeaGenerator.Generate(symbol: "BTCUSDT"), closed: -0.01);

        IdeaPageResponse crypto = await Ideas("?state=closed&kind=crypto");
        IdeaPageResponse stocks = await Ideas("?state=closed&kind=stock");
        IdeaPageResponse all = await Ideas("?state=closed");

        Assert.That(crypto.Items.Select(idea => idea.Id), Is.EqualTo(new[] { btc.Id }));
        Assert.That(stocks.Total, Is.Zero);
        Assert.That(all.Items.Select(idea => idea.Id), Is.EquivalentTo(new[] { gone.Id, btc.Id }));
    }

    // Scenario «Вид в числах».
    [Test]
    public async Task The_summary_and_the_counts_by_strategy_count_only_the_ideas_of_the_kind()
    {
        await Snapshot();
        var fundingShort = Guid.NewGuid();
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "BTCUSDT"), closed: 0.01);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "ETHUSDT"), closed: 0.02);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "TSLAUSDT"), closed: -0.01);
        await Add(IdeaGenerator.Generate(symbol: "BTCUSDT"));
        await Add(IdeaGenerator.Generate(symbol: "TSLAUSDT"));
        await Add(IdeaGenerator.Generate(name: "breakout", symbol: "NVDAUSDT"));

        IdeaSummaryPageResponse summary = (await _api.Http.GetFromJsonAsync<IdeaSummaryPageResponse>("/api/ideas/summary?kind=stock", Json))!;
        IdeaSummaryPageResponse cryptoSummary = (await _api.Http.GetFromJsonAsync<IdeaSummaryPageResponse>("/api/ideas/summary?kind=crypto", Json))!;
        IdeaStrategyCountsResponse open = (await _api.Http.GetFromJsonAsync<IdeaStrategyCountsResponse>("/api/ideas/strategies?kind=stock", Json))!;
        IdeaStrategyCountsResponse closed = (await _api.Http.GetFromJsonAsync<IdeaStrategyCountsResponse>("/api/ideas/strategies?state=closed&kind=crypto", Json))!;

        Assert.That(summary.Items.Select(row => (row.Strategy, row.Ideas)), Is.EqualTo(new[] { ("funding-short", 1) }));
        Assert.That(summary.Items.Single().Mean, Is.EqualTo(-0.01).Within(1e-12));
        Assert.That(cryptoSummary.Items.Select(row => (row.Strategy, row.Ideas)), Is.EqualTo(new[] { ("funding-short", 2) }));
        Assert.That(open.Total, Is.EqualTo(2));
        Assert.That(open.Strategies.Select(row => (row.Strategy, row.Ideas)), Is.EqualTo(new[] { ("breakout", 1), ("funding-short", 1) }));
        Assert.That((closed.Total, closed.Strategies.Select(row => (row.Strategy, row.Ideas))), Is.EqualTo((2, new[] { ("funding-short", 2) })).Using<(int, IEnumerable<(string, int)>)>(Same));
    }

    // Scenario «Неизвестный вид» of «Список идей».
    [TestCase("/api/ideas?kind=linear")]
    [TestCase("/api/ideas?state=closed&kind=linear")]
    [TestCase("/api/ideas/summary?kind=linear")]
    [TestCase("/api/ideas/strategies?kind=linear")]
    public async Task An_unknown_kind_of_the_ideas_is_refused_at_its_field(string url)
    {
        HttpResponseMessage response = await _api.Http.GetAsync(url);

        Assert.That(await Errors(response), Is.EqualTo(new[] { ("InvalidChoice", (string?)"kind") }));
    }

    // Spec market-overview «Страница пары»: the ideas of a pair take no kind and give all its ideas.
    [Test]
    public async Task The_ideas_of_a_pair_take_no_kind()
    {
        await Snapshot();
        await Add(IdeaGenerator.Generate(symbol: "TSLAUSDT"));
        await Add(IdeaGenerator.Generate(symbol: "TSLAUSDT", moment: IdeaGenerator.Noon.AddDays(-1)), closed: 0.01);

        IdeaPageResponse crypto = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/market/pairs/TSLAUSDT/ideas?kind=crypto", Json))!;
        IdeaPageResponse wrong = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/market/pairs/TSLAUSDT/ideas?kind=linear", Json))!;

        Assert.That(crypto.Total, Is.EqualTo(2));
        Assert.That(wrong.Total, Is.EqualTo(2));
    }

    private Task Snapshot()
    {
        return BenchServices.Get<IMarketPairRepository>().ReplaceAsync(
            [
                Pair("BTCUSDT", InstrumentKind.Crypto), Pair("ETHUSDT", InstrumentKind.Crypto), Pair("TSLAUSDT", InstrumentKind.Stock),
                Pair("NVDAUSDT", InstrumentKind.Stock)
            ],
            CancellationToken.None);
    }

    private async Task<MarketPageResponse> Market(string query)
    {
        HttpResponseMessage response = await _api.Http.GetAsync($"/api/market/pairs{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MarketPageResponse>(Json))!;
    }

    private async Task<IdeaPageResponse> Ideas(string query)
    {
        HttpResponseMessage response = await _api.Http.GetAsync($"/api/ideas{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdeaPageResponse>(Json))!;
    }

    private static MarketPair Pair(string symbol, InstrumentKind kind, decimal turnover = 1_000_000m, decimal funding = 0.0001m)
    {
        return new MarketPair(symbol, 100, 0, 0, 0, turnover, 1000, 100_000, funding, null, Now, kind);
    }

    private static async Task<Idea> Add(Idea idea, double? closed = null)
    {
        IIdeaRepository ideas = BenchServices.Get<IIdeaRepository>();
        await ideas.AddAsync(idea, CancellationToken.None);
        if (closed is { } result)
        {
            idea.State = IdeaState.Closed;
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-1);
            idea.ExitReason = ExitReason.Time;
            idea.Result = result;
            idea.ResultUsdt = result * 100;
            await ideas.SaveAsync(idea, CancellationToken.None);
        }

        return idea;
    }

    private static bool Same<T>((int, IEnumerable<T>) a, (int, IEnumerable<T>) b)
    {
        return a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2);
    }

    private static async Task<(string Code, string? Field)[]> Errors(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Select(error => (error.Code, error.Field))];
    }
}
