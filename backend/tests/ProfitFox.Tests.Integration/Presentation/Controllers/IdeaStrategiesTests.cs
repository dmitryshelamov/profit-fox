using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-setup-strategies, task 5.1: spec strategy-ideas «Число идей по стратегиям» through
///     <c>GET /api/ideas/strategies</c>: the choices of the list but the strategy, a row for every strategy of a category of
///     ideas and for every strategy of the ideas counted.
/// </summary>
[NonParallelizable]
public sealed class IdeaStrategiesTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;
    private int _moments;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market);
        Guid signal = await CreateSignal();
        foreach (string name in new[] { "accumulation", "breakout", "false-breakout" })
        {
            await CreateStrategy(name, StrategiesTests.Strategy(signal), "idea");
        }

        await CreateStrategy("ladder", JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ladder.json")))!.AsObject(), "bot");
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    // «Три стратегии».
    [Test]
    public async Task The_open_ideas_by_strategy_with_a_row_of_nought_for_a_strategy_without_ideas()
    {
        await Open("breakout", 17);
        await Open("false-breakout", 35);

        IdeaStrategyCountsResponse counts = await Counts("");

        Assert.That(counts.Total, Is.EqualTo(52));
        Assert.That(Rows(counts), Is.EqualTo(new[] { ("accumulation", 0), ("breakout", 17), ("false-breakout", 35) }));
        Assert.That(counts.FilterGone, Is.False);
    }

    // «Выбор стратегии не сужает счёт».
    [Test]
    public async Task The_choice_of_a_strategy_does_not_narrow_the_count_but_the_side_does()
    {
        await Open("breakout", 10, EntrySide.Short);
        await Open("breakout", 7, EntrySide.Long);
        await Open("false-breakout", 5, EntrySide.Short);

        IdeaStrategyCountsResponse counts = await Counts("?strategy=breakout&side=short");

        Assert.That(counts.Total, Is.EqualTo(15));
        Assert.That(Rows(counts), Is.EqualTo(new[] { ("accumulation", 0), ("breakout", 10), ("false-breakout", 5) }));
    }

    // «Период журнала».
    [Test]
    public async Task The_closed_ideas_of_the_last_seven_days()
    {
        await Closed("breakout", 2, exitDaysAgo: 3);
        await Closed("breakout", 4, exitDaysAgo: 10);
        await Closed("false-breakout", 1, exitDaysAgo: 6);
        await Open("breakout", 3);

        IdeaStrategyCountsResponse week = await Counts("?state=closed&period=7");
        IdeaStrategyCountsResponse all = await Counts("?state=closed");

        Assert.That(Rows(week), Is.EqualTo(new[] { ("accumulation", 0), ("breakout", 2), ("false-breakout", 1) }));
        Assert.That((week.Total, all.Total), Is.EqualTo((3, 7)));
    }

    // «Удалённая стратегия».
    [Test]
    public async Task A_strategy_that_is_gone_has_the_row_of_its_idea()
    {
        await Open("old-breakout", 1);

        IdeaStrategyCountsResponse counts = await Counts("");

        Assert.That(Rows(counts), Is.EqualTo(new[] { ("accumulation", 0), ("breakout", 0), ("false-breakout", 0), ("old-breakout", 1) }));
    }

    // «Стратегия для бота».
    [Test]
    public async Task A_strategy_for_a_bot_has_no_row()
    {
        IdeaStrategyCountsResponse counts = await Counts("");

        Assert.That(counts.Strategies.Select(row => row.Strategy), Has.None.EqualTo("ladder"));
        Assert.That(counts.Total, Is.Zero);
    }

    [TestCase("?side=up", "side")]
    [TestCase("?state=paused", "state")]
    [TestCase("?exitReason=stops", "exitReason")]
    [TestCase("?timeframe=2h", "timeframe")]
    public async Task A_choice_outside_its_list_is_refused(string query, string field)
    {
        HttpResponseMessage response = await _api.Http.GetAsync($"/api/ideas/strategies{query}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        ErrorResponse errors = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => error.Field), Is.EqualTo(new[] { field }));
    }

    private static (string, int)[] Rows(IdeaStrategyCountsResponse counts)
    {
        return [.. counts.Strategies.Select(row => (row.Strategy, row.Ideas))];
    }

    private async Task<IdeaStrategyCountsResponse> Counts(string query)
    {
        HttpResponseMessage response = await _api.Http.GetAsync($"/api/ideas/strategies{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdeaStrategyCountsResponse>(Json))!;
    }

    private async Task Open(string strategy, int count, EntrySide side = EntrySide.Short)
    {
        for (int index = 0; index < count; index++)
        {
            await BenchServices.Get<IIdeaRepository>().AddAsync(Next(strategy, side), CancellationToken.None);
        }
    }

    private async Task Closed(string strategy, int count, int exitDaysAgo)
    {
        IIdeaRepository ideas = BenchServices.Get<IIdeaRepository>();
        for (int index = 0; index < count; index++)
        {
            Idea idea = Next(strategy, EntrySide.Short);
            await ideas.AddAsync(idea, CancellationToken.None);
            idea.State = IdeaState.Closed;
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-exitDaysAgo);
            idea.ExitReason = ExitReason.Time;
            idea.Result = 0.001;
            idea.ResultUsdt = 0.1;
            await ideas.SaveAsync(idea, CancellationToken.None);
        }
    }

    // An idea on a candle of its own: a strategy has one idea on a pair, a timeframe and a candle.
    private Idea Next(string strategy, EntrySide side)
    {
        _moments++;
        return IdeaGenerator.Generate(name: strategy, side: side, moment: IdeaGenerator.Noon.AddHours(-4 * _moments));
    }

    private async Task<Guid> CreateSignal()
    {
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = "funding-high", ["graph"] = ReferenceSignals.H0001(), ["layout"] = new JsonObject() }, Json);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<SignalResponse>(Json))!.Id;
    }

    private async Task CreateStrategy(string name, JsonObject graph, string kind)
    {
        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = name, ["graph"] = graph, ["layout"] = new JsonObject(), ["kind"] = kind }, Json);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
    }
}
