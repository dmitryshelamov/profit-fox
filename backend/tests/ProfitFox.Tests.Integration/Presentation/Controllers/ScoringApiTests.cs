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
using ProfitFox.Presentation.Contracts.Scoring;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-idea-score, task 2.7 (design.md, decision 3): the labels and their weights, the weights of one label with
///     200, 404 and 422, the score of an idea in the lists with its sort and «балл от», and the outcomes by score through the
///     API.
/// </summary>
[NonParallelizable]
public sealed class ScoringApiTests
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

    [Test]
    public async Task The_labels_come_with_their_weights_and_the_built_in_label_last()
    {
        await Label("btc-down-4h", true, "↓ BTC 4h", "4h");
        await Label("pair-down", false, "↓ пара", "1h");

        HttpResponseMessage set = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "↓ BTC 4h", longWeight = -2, shortWeight = 1 }, Json);
        HttpResponseMessage burst = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "⚡ всплеск", longWeight = 0, shortWeight = -1 }, Json);
        ScoringLabelResponse[] labels = (await _api.Http.GetFromJsonAsync<ScoringLabelResponse[]>("/api/scoring/labels", Json))!;

        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK), await set.Content.ReadAsStringAsync());
        Assert.That(await set.Content.ReadFromJsonAsync<ScoringLabelResponse>(Json), Is.EqualTo(new ScoringLabelResponse("↓ BTC 4h", true, "4h", "btc-down-4h", -2, 1, false)));
        Assert.That(burst.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(labels, Is.EqualTo(new[]
        {
            new ScoringLabelResponse("↓ BTC 4h", true, "4h", "btc-down-4h", -2, 1, false),
            new ScoringLabelResponse("↓ пара", false, "1h", "pair-down", 0, 0, false),
            new ScoringLabelResponse("⚡ всплеск", true, null, null, 0, -1, true)
        }));
    }

    [Test]
    // Change decimal-label-weights, task 2.2: spec idea-score «Вес вне диапазона», «Вес дальше сотых».
    public async Task A_weight_out_of_range_or_past_the_hundredths_answers_422_and_a_text_without_a_label_404()
    {
        await Label("btc-down-4h", true, "↓ BTC 4h", "4h");

        HttpResponseMessage high = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "↓ BTC 4h", longWeight = 101, shortWeight = 0 }, Json);
        HttpResponseMessage thousandths = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "↓ BTC 4h", longWeight = 0, shortWeight = 0.125 }, Json);
        HttpResponseMessage missing = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "нет такой", longWeight = 1, shortWeight = 1 }, Json);

        Assert.That(await Errors(high), Is.EqualTo(new[] { ("InvalidWeight", (string?)"longWeight") }));
        Assert.That(await Errors(thousandths), Is.EqualTo(new[] { ("InvalidWeight", (string?)"shortWeight") }));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        ScoringLabelResponse[] labels = (await _api.Http.GetFromJsonAsync<ScoringLabelResponse[]>("/api/scoring/labels", Json))!;
        Assert.That(labels.Select(label => (label.Text, label.LongWeight, label.ShortWeight)), Is.EqualTo(new[] { ("↓ BTC 4h", 0m, 0m), ("⚡ всплеск", 0m, 0m) }), "nothing changed");
    }

    // Change decimal-label-weights, task 2.3: spec idea-score «Дробный вес» through the API, as a number of JSON.
    [Test]
    public async Task A_weight_with_hundredths_is_saved_and_the_labels_give_it_as_a_number()
    {
        await Label("btc-down-15m", true, "↓ BTC 15m", "15m");

        HttpResponseMessage set = await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "↓ BTC 15m", longWeight = -0.5, shortWeight = 0.25 }, Json);
        JsonArray labels = (await _api.Http.GetFromJsonAsync<JsonArray>("/api/scoring/labels", Json))!;

        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK), await set.Content.ReadAsStringAsync());
        JsonNode row = labels.Single(label => (string?)label!["text"] == "↓ BTC 15m")!;
        Assert.That((row["longWeight"]!.GetValueKind(), (decimal)row["longWeight"]!, (decimal)row["shortWeight"]!), Is.EqualTo((JsonValueKind.Number, -0.5m, 0.25m)));
    }

    [Test]
    public async Task A_renamed_label_takes_its_weights_and_an_idea_with_the_former_text_scores_nought()
    {
        await Label("btc-down", true, "↓ BTC", "5m");
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "↓ BTC", longWeight = -2, shortWeight = 0 }, Json);
        await Add(IdeaGenerator.Generate(side: EntrySide.Long, labels: ["↓ BTC"]));

        await Label("btc-down", true, "↓ BTC 5m", "5m", create: false);

        ScoringLabelResponse[] labels = (await _api.Http.GetFromJsonAsync<ScoringLabelResponse[]>("/api/scoring/labels", Json))!;
        IdeaPageResponse open = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas", Json))!;
        Assert.That(labels[0], Is.EqualTo(new ScoringLabelResponse("↓ BTC 5m", true, "5m", "btc-down", -2, 0, false)));
        Assert.That(open.Items.Single().Score, Is.Zero);
    }

    [Test]
    public async Task The_lists_carry_the_score_sort_by_it_and_take_a_least_score()
    {
        await Label("plus-three", true, "плюс 3", "1h");
        await Label("plus-two", true, "плюс 2", "1h");
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "плюс 3", longWeight = 0, shortWeight = 3 }, Json);
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "плюс 2", longWeight = 0, shortWeight = 2 }, Json);
        await Add(IdeaGenerator.Generate(symbol: "AUSDT", labels: ["плюс 3"]));
        await Add(IdeaGenerator.Generate(symbol: "BUSDT", labels: ["плюс 2"]));
        await Add(IdeaGenerator.Generate(symbol: "CUSDT"));
        await Add(IdeaGenerator.Generate(symbol: "DUSDT", labels: ["плюс 2"]), closed: 0.004);
        await Add(IdeaGenerator.Generate(symbol: "EUSDT"), closed: -0.002);

        IdeaPageResponse sorted = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?sort=score&direction=desc", Json))!;
        IdeaPageResponse fromTwo = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?minScore=2", Json))!;
        IdeaSummaryPageResponse summary = (await _api.Http.GetFromJsonAsync<IdeaSummaryPageResponse>("/api/ideas/summary?minScore=2", Json))!;
        IdeaStrategyCountsResponse counts = (await _api.Http.GetFromJsonAsync<IdeaStrategyCountsResponse>("/api/ideas/strategies?minScore=-1", Json))!;
        IdeaScoresResponse scores = (await _api.Http.GetFromJsonAsync<IdeaScoresResponse>("/api/ideas/scores", Json))!;
        HttpResponseMessage word = await _api.Http.GetAsync("/api/ideas?minScore=abc");
        HttpResponseMessage strategiesWord = await _api.Http.GetAsync("/api/ideas/strategies?minScore=0.125");
        HttpResponseMessage scoresWord = await _api.Http.GetAsync("/api/ideas/scores?minScore=abc");

        Assert.That(sorted.Items.Select(idea => (idea.Symbol, idea.Score)), Is.EqualTo(new[] { ("AUSDT", 3m), ("BUSDT", 2m), ("CUSDT", 0m) }));
        Assert.That((fromTwo.Total, fromTwo.Items.Select(idea => idea.Symbol).Order().ToArray()), Is.EqualTo((2, new[] { "AUSDT", "BUSDT" })).Using<(int, string[])>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(summary.Items.Single().Ideas, Is.EqualTo(1));
        Assert.That(counts.Total, Is.EqualTo(3));
        Assert.That(scores.Rows.Select(row => (row.Score, row.Ideas)), Is.EqualTo(new[] { (2, 1), (0, 1) }));
        Assert.That(await Errors(word), Is.EqualTo(new[] { ("InvalidMinScore", (string?)"minScore") }));
        Assert.That(await Errors(strategiesWord), Is.EqualTo(new[] { ("InvalidMinScore", (string?)"minScore") }));
        Assert.That(await Errors(scoresWord), Is.EqualTo(new[] { ("InvalidMinScore", (string?)"minScore") }));
    }

    // Change decimal-label-weights, task 2.5: spec idea-score «Дробный порог» through the API.
    [Test]
    public async Task A_least_score_with_hundredths_takes_the_ideas_with_at_least_that_score()
    {
        await Label("plus-one-and-a-half", true, "плюс 1.5", "1h");
        await Label("plus-one-and-a-quarter", true, "плюс 1.25", "1h");
        await Label("plus-three-quarters", true, "плюс 0.75", "1h");
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "плюс 1.5", longWeight = 0, shortWeight = 1.5 }, Json);
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "плюс 1.25", longWeight = 0, shortWeight = 1.25 }, Json);
        await _api.Http.PutAsJsonAsync("/api/scoring/weights", new { text = "плюс 0.75", longWeight = 0, shortWeight = 0.75 }, Json);
        await Add(IdeaGenerator.Generate(symbol: "AUSDT", side: EntrySide.Short, labels: ["плюс 1.5"]));
        await Add(IdeaGenerator.Generate(symbol: "BUSDT", side: EntrySide.Short, labels: ["плюс 1.25"]));
        await Add(IdeaGenerator.Generate(symbol: "CUSDT", side: EntrySide.Short, labels: ["плюс 0.75"]));

        IdeaPageResponse page = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?minScore=1.25&sort=score&direction=desc", Json))!;
        IdeaStrategyCountsResponse counts = (await _api.Http.GetFromJsonAsync<IdeaStrategyCountsResponse>("/api/ideas/strategies?minScore=1.25", Json))!;

        Assert.That(page.Items.Select(idea => (idea.Symbol, idea.Score)), Is.EqualTo(new[] { ("AUSDT", 1.5m), ("BUSDT", 1.25m) }));
        Assert.That(counts.Total, Is.EqualTo(2));
    }

    private static async Task<(string Code, string? Field)[]> Errors(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Select(error => (error.Code, error.Field))];
    }

    private async Task Label(string signal, bool enabled, string text, string timeframe, bool create = true)
    {
        if (create)
        {
            HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/signals", new JsonObject { ["name"] = signal, ["graph"] = ReferenceSignals.H0001(), ["layout"] = new JsonObject() }, Json);
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        }

        HttpResponseMessage labelled = await _api.Http.PutAsJsonAsync($"/api/signals/{signal}/label", new SignalLabelRequest(enabled, text, timeframe), Json);
        Assert.That(labelled.StatusCode, Is.EqualTo(HttpStatusCode.OK), await labelled.Content.ReadAsStringAsync());
    }

    private static async Task Add(Idea idea, double? closed = null)
    {
        IIdeaRepository ideas = BenchServices.Get<IIdeaRepository>();
        await ideas.AddAsync(idea, CancellationToken.None);
        if (closed is { } result)
        {
            idea.EntryPrice = 100;
            idea.EntryTime = idea.Moment;
            idea.State = IdeaState.Closed;
            idea.ExitTime = DateTimeOffset.UtcNow.AddHours(-1);
            idea.ExitReason = ExitReason.Time;
            idea.Result = result;
            idea.ResultUsdt = result * 100;
            await ideas.SaveAsync(idea, CancellationToken.None);
        }
    }
}
