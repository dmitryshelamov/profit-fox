using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     Change add-idea-score, tasks 2.5 and 2.6 (design.md, decision 2): the score of an idea counted by the store from the
///     weights now — in the list, its order and «балл от», in a condition of a filter of «Идеи» and of «Рынок» — and the
///     outcomes of closed ideas by score (spec idea-score «Балл идеи», «Балл в списках идей», «Итоги по баллу»; spec
///     saved-filters «Балл в фильтре»).
/// </summary>
[NonParallelizable]
public sealed class IdeaScoreTests
{
    private IIdeaRepository _ideas = null!;
    private ILabelWeightRepository _weights = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _ideas = BenchServices.Get<IIdeaRepository>();
        _weights = BenchServices.Get<ILabelWeightRepository>();
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", -2, 1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("оборот ↑ 1h", 1, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("⚡ всплеск", 0, -1), CancellationToken.None);
    }

    [Test]
    public async Task A_long_with_three_labels_scores_the_sum_of_their_long_weights_and_a_short_that_of_their_short_weights()
    {
        string[] labels = ["↓ BTC 4h", "оборот ↑ 1h", "⚡ всплеск"];
        Idea buy = await Add(EntrySide.Long, labels, "BTCUSDT");
        Idea sell = await Add(EntrySide.Short, labels, "ETHUSDT");
        Idea bare = await Add(EntrySide.Long, [], "SOLUSDT");
        Idea unknown = await Add(EntrySide.Long, ["без веса"], "XRPUSDT");

        Dictionary<Guid, decimal> scores = await Scores(IdeaListKind.Open);

        Assert.That((scores[buy.Id], scores[sell.Id], scores[bare.Id], scores[unknown.Id]), Is.EqualTo((-1m, 0m, 0m, 0m)));
    }

    // Change decimal-label-weights, task 2.4: spec idea-score «Дробные веса»; the sum is exact to the hundredths.
    [Test]
    public async Task A_long_with_weights_of_hundredths_scores_their_exact_sum()
    {
        await _weights.SetAsync(new LabelWeight("цена ↓ 4h", 1, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("цена ↓ 1h", 0.5m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC 15m", -0.25m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("цена ↑ 5m", -0.14m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 0.1", 0.1m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 0.2", 0.2m, 0), CancellationToken.None);
        Idea idea = await Add(EntrySide.Long, ["цена ↓ 4h", "цена ↓ 1h", "↓ BTC 15m", "цена ↑ 5m"], "AUSDT");
        Idea tenths = await Add(EntrySide.Long, ["плюс 0.1", "плюс 0.2"], "BUSDT");

        Dictionary<Guid, decimal> scores = await Scores(IdeaListKind.Open);

        Assert.That((scores[idea.Id], scores[tenths.Id]), Is.EqualTo((1.11m, 0.3m)));
        Assert.That((await _ideas.FindAsync(idea.Id, CancellationToken.None))!.Score, Is.EqualTo(1.11m));
    }

    [Test]
    public async Task A_change_of_a_weight_changes_the_score_of_a_closed_idea_but_not_the_idea()
    {
        Idea idea = await Close(await Add(EntrySide.Long, ["↓ BTC 4h", "оборот ↑ 1h", "⚡ всплеск"]), 0.004);
        Assert.That((await Scores(IdeaListKind.Closed))[idea.Id], Is.EqualTo(-1m));

        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", 0, 1), CancellationToken.None);

        Assert.That((await Scores(IdeaListKind.Closed))[idea.Id], Is.EqualTo(1m));
        Idea read = (await _ideas.FindAsync(idea.Id, CancellationToken.None))!;
        Assert.That((read.Result, read.Labels, read.State), Is.EqualTo(((double?)0.004, idea.Labels, IdeaState.Closed)));
    }

    [Test]
    public async Task Open_ideas_sort_by_score_and_those_below_the_least_score_are_left_out()
    {
        await Weights(("плюс 3", 3), ("плюс 2", 2), ("минус 1", -1));
        Idea three = await Add(EntrySide.Long, ["плюс 3"], "AUSDT");
        Idea two = await Add(EntrySide.Long, ["плюс 2"], "BUSDT");
        Idea nought = await Add(EntrySide.Long, [], "CUSDT");
        Idea minus = await Add(EntrySide.Long, ["минус 1"], "DUSDT");

        ListPage<Idea> sorted = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("score", true, 1, 50)), CancellationToken.None);
        ListPage<Idea> fromTwo = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50), MinScore: 2), CancellationToken.None);
        ListPage<Idea> fromMinusOne = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("score", true, 1, 50), MinScore: -1), CancellationToken.None);

        Assert.That(sorted.Items.Select(idea => (idea.Id, idea.Score)), Is.EqualTo(new[] { (three.Id, 3m), (two.Id, 2m), (nought.Id, 0m), (minus.Id, -1m) }));
        Assert.That((fromTwo.Total, fromTwo.Items.Select(idea => idea.Score).Order().ToArray()), Is.EqualTo((2, new[] { 2m, 3m })).Using<(int, decimal[])>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(fromMinusOne.Total, Is.EqualTo(4));
    }

    // Change decimal-label-weights, task 2.5: spec idea-score «Дробный порог».
    [Test]
    public async Task Open_ideas_below_a_least_score_with_hundredths_are_left_out()
    {
        await _weights.SetAsync(new LabelWeight("плюс 1.5", 1.5m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 1.25", 1.25m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 0.75", 0.75m, 0), CancellationToken.None);
        Idea high = await Add(EntrySide.Long, ["плюс 1.5"], "AUSDT");
        Idea even = await Add(EntrySide.Long, ["плюс 1.25"], "BUSDT");
        await Add(EntrySide.Long, ["плюс 0.75"], "CUSDT");

        ListPage<Idea> page = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("score", true, 1, 50), MinScore: 1.25m), CancellationToken.None);

        Assert.That(page.Items.Select(idea => (idea.Id, idea.Score)), Is.EqualTo(new[] { (high.Id, 1.5m), (even.Id, 1.25m) }));
    }

    [Test]
    public async Task The_least_score_narrows_the_summary_and_the_counts_by_strategy()
    {
        await Weights(("плюс 2", 2));
        await Close(await Add(EntrySide.Long, ["плюс 2"], "AUSDT"), 0.006);
        await Close(await Add(EntrySide.Long, [], "BUSDT"), -0.004);

        ListPage<IdeaSummary> summary = await _ideas.SummaryAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("ideas", true, 1, 50), MinScore: 2), CancellationToken.None);
        IReadOnlyList<IdeaStrategyCount> counts = await _ideas.StrategyCountsAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("exitTime", true, 1, 50), MinScore: 2), CancellationToken.None);

        Assert.That(summary.Items.Single().Ideas, Is.EqualTo(1));
        Assert.That(counts.Single().Ideas, Is.EqualTo(1));
    }

    [Test]
    public async Task A_condition_of_a_filter_on_the_score_takes_the_ideas_with_at_least_that_score()
    {
        await Weights(("плюс 3", 3), ("плюс 2", 2));
        Idea three = await Add(EntrySide.Long, ["плюс 3"], "AUSDT");
        Idea two = await Add(EntrySide.Long, ["плюс 2"], "BUSDT");
        await Add(EntrySide.Long, [], "CUSDT");

        ListPage<Idea> page = await _ideas.ListAsync(
            new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50), Conditions: [new FilterCondition("score", "ge", "2")]),
            CancellationToken.None);

        Assert.That(page.Items.Select(idea => idea.Id), Is.EquivalentTo(new[] { three.Id, two.Id }));
    }

    // Change decimal-label-weights, task 2.6: spec saved-filters «Дробный балл в фильтре».
    [Test]
    public async Task A_condition_of_a_filter_on_a_score_with_hundredths_takes_the_ideas_with_at_least_that_score()
    {
        await _weights.SetAsync(new LabelWeight("плюс 1.5", 1.5m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 1.25", 1.25m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 1.24", 1.24m, 0), CancellationToken.None);
        Idea high = await Add(EntrySide.Long, ["плюс 1.5"], "AUSDT");
        Idea even = await Add(EntrySide.Long, ["плюс 1.25"], "BUSDT");
        await Add(EntrySide.Long, ["плюс 1.24"], "CUSDT");

        ListPage<Idea> page = await _ideas.ListAsync(
            new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50), Conditions: [new FilterCondition("score", "ge", "1.25")]),
            CancellationToken.None);

        Assert.That(page.Items.Select(idea => idea.Id), Is.EquivalentTo(new[] { high.Id, even.Id }));
    }

    [Test]
    public async Task A_filter_of_the_market_on_the_score_takes_the_pairs_with_an_open_idea_of_that_score()
    {
        await Weights(("плюс 2", 2));
        IMarketPairRepository pairs = BenchServices.Get<IMarketPairRepository>();
        DateTimeOffset now = IdeaGenerator.Noon;
        await pairs.ReplaceAsync([Pair("AUSDT", now), Pair("BUSDT", now), Pair("CUSDT", now)], CancellationToken.None);
        await Add(EntrySide.Long, ["плюс 2"], "AUSDT");
        await Add(EntrySide.Long, [], "BUSDT");

        ListPage<MarketPairRow> page = await pairs.ListAsync(
            new MarketListQuery(new ListOrder("symbol", false, 1, 50), Conditions: [new FilterCondition("score", "ge", "2")]),
            CancellationToken.None);

        Assert.That(page.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "AUSDT" }));
    }

    // Change decimal-label-weights, task 3.1 (design.md, decision 4): spec idea-score «Два балла» — groups of 0.5 by their
    // bounds, from the highest.
    [Test]
    public async Task The_outcomes_by_score_count_closed_ideas_from_the_highest_score()
    {
        await Weights(("плюс 2", 2));
        await Close(await Add(EntrySide.Long, ["плюс 2"], "AUSDT"), 0.006, 0.6);
        await Close(await Add(EntrySide.Long, ["плюс 2"], "BUSDT"), -0.002, -0.2);
        await Close(await Add(EntrySide.Long, [], "CUSDT"), -0.004, -0.4);
        await Add(EntrySide.Long, ["плюс 2"], "DUSDT");

        IReadOnlyList<IdeaScoreRow> rows = await _ideas.ScoresAsync(Closed(), CancellationToken.None);

        Assert.That(rows.Select(row => (row.From, row.To, row.Ideas)), Is.EqualTo(new[] { (2m, 2.5m, 2), (0m, 0.5m, 1) }));
        Assert.That(rows[0].Wins, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(rows[0].Mean, Is.EqualTo(0.002).Within(1e-9));
        Assert.That(rows[0].Sum, Is.EqualTo(0.4).Within(1e-9));
        Assert.That((rows[1].Wins, rows[1].Mean), Is.EqualTo((0.0, -0.004)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9));
    }

    [Test]
    public async Task The_outcomes_by_score_take_the_side_chosen_and_leave_out_an_excluded_idea()
    {
        await Weights(("плюс 1", 1));
        await _weights.SetAsync(new LabelWeight("плюс 1", 1, 1), CancellationToken.None);
        await Close(await Add(EntrySide.Long, ["плюс 1"], "AUSDT"), 0.001);
        await Close(await Add(EntrySide.Short, ["плюс 1"], "BUSDT"), 0.002);
        Idea excluded = await Add(EntrySide.Long, ["плюс 1"], "CUSDT");
        excluded.State = IdeaState.Excluded;
        excluded.Excluded = "нет данных";
        await _ideas.SaveAsync(excluded, CancellationToken.None);

        IReadOnlyList<IdeaScoreRow> all = await _ideas.ScoresAsync(Closed(), CancellationToken.None);
        IReadOnlyList<IdeaScoreRow> shorts = await _ideas.ScoresAsync(Closed() with { Side = "short" }, CancellationToken.None);

        Assert.That(all.Select(row => (row.From, row.To, row.Ideas)), Is.EqualTo(new[] { (1m, 1.5m, 2) }));
        Assert.That(shorts.Select(row => (row.From, row.Ideas, row.Mean)), Is.EqualTo(new[] { (1m, 1, 0.002) }));
    }

    // Change decimal-label-weights, task 3.1: spec idea-score «Дробные баллы в одной группе», «Отрицательный дробный балл» —
    // a group takes the scores from its lower bound up to, not including, its upper one.
    [Test]
    public async Task The_outcomes_by_score_put_scores_with_hundredths_in_groups_of_a_half()
    {
        await _weights.SetAsync(new LabelWeight("плюс 0.5", 0.5m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 0.86", 0.86m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("плюс 1", 1, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("минус 0.25", -0.25m, 0), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("минус 0.5", -0.5m, 0), CancellationToken.None);
        await Close(await Add(EntrySide.Long, ["плюс 0.5"], "AUSDT"), 0.001);
        await Close(await Add(EntrySide.Long, ["плюс 0.86"], "BUSDT"), 0.001);
        await Close(await Add(EntrySide.Long, ["плюс 1"], "CUSDT"), 0.001);
        await Close(await Add(EntrySide.Long, ["минус 0.25"], "DUSDT"), 0.001);
        await Close(await Add(EntrySide.Long, ["минус 0.5"], "EUSDT"), 0.001);

        IReadOnlyList<IdeaScoreRow> rows = await _ideas.ScoresAsync(Closed(), CancellationToken.None);

        Assert.That(rows.Select(row => (row.From, row.To, row.Ideas)), Is.EqualTo(new[] { (1m, 1.5m, 1), (0.5m, 1m, 2), (-0.5m, 0m, 2) }));
    }

    [Test]
    public async Task After_a_change_of_a_weight_the_outcomes_put_the_ideas_at_their_new_scores()
    {
        await Weights(("плюс 1", 1));
        await Close(await Add(EntrySide.Long, ["плюс 1"], "AUSDT"), 0.001);
        Assert.That((await _ideas.ScoresAsync(Closed(), CancellationToken.None)).Single().From, Is.EqualTo(1m));

        await _weights.SetAsync(new LabelWeight("плюс 1", 3, 0), CancellationToken.None);

        Assert.That((await _ideas.ScoresAsync(Closed(), CancellationToken.None)).Single().From, Is.EqualTo(3m));
    }

    private static IdeaListQuery Closed()
    {
        return new IdeaListQuery(IdeaListKind.Closed, new ListOrder("exitTime", true, 1, 50));
    }

    private async Task Weights(params (string Text, int Long)[] weights)
    {
        foreach ((string text, int weight) in weights)
        {
            await _weights.SetAsync(new LabelWeight(text, weight, 0), CancellationToken.None);
        }
    }

    private async Task<Idea> Add(EntrySide side, string[] labels, string symbol = "BTCUSDT")
    {
        Idea idea = IdeaGenerator.Generate(symbol: symbol, side: side, labels: labels);
        await _ideas.AddAsync(idea, CancellationToken.None);
        return idea;
    }

    private async Task<Idea> Close(Idea idea, double result, double? usdt = null)
    {
        idea.EntryPrice = 100;
        idea.EntryTime = idea.Moment;
        idea.State = IdeaState.Closed;
        idea.ExitTime = DateTimeOffset.UtcNow.AddHours(-1);
        idea.ExitReason = ExitReason.Time;
        idea.Result = result;
        idea.ResultUsdt = usdt ?? result * 100;
        await _ideas.SaveAsync(idea, CancellationToken.None);
        return idea;
    }

    private async Task<Dictionary<Guid, decimal>> Scores(IdeaListKind kind)
    {
        ListPage<Idea> page = await _ideas.ListAsync(new IdeaListQuery(kind, new ListOrder("moment", true, 1, 200)), CancellationToken.None);
        return page.Items.ToDictionary(idea => idea.Id, idea => idea.Score);
    }

    private static MarketPair Pair(string symbol, DateTimeOffset at)
    {
        return new MarketPair(symbol, 1m, 0.01m, 0.02m, null, 1_000_000m, 24_000m, 24_000m, 0.0001m, at.AddHours(2), at);
    }
}
