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

        Dictionary<Guid, int> scores = await Scores(IdeaListKind.Open);

        Assert.That((scores[buy.Id], scores[sell.Id], scores[bare.Id], scores[unknown.Id]), Is.EqualTo((-1, 0, 0, 0)));
    }

    [Test]
    public async Task A_change_of_a_weight_changes_the_score_of_a_closed_idea_but_not_the_idea()
    {
        Idea idea = await Close(await Add(EntrySide.Long, ["↓ BTC 4h", "оборот ↑ 1h", "⚡ всплеск"]), 0.004);
        Assert.That((await Scores(IdeaListKind.Closed))[idea.Id], Is.EqualTo(-1));

        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", 0, 1), CancellationToken.None);

        Assert.That((await Scores(IdeaListKind.Closed))[idea.Id], Is.EqualTo(1));
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

        Assert.That(sorted.Items.Select(idea => (idea.Id, idea.Score)), Is.EqualTo(new[] { (three.Id, 3), (two.Id, 2), (nought.Id, 0), (minus.Id, -1) }));
        Assert.That((fromTwo.Total, fromTwo.Items.Select(idea => idea.Score).Order().ToArray()), Is.EqualTo((2, new[] { 2, 3 })).Using<(int, int[])>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(fromMinusOne.Total, Is.EqualTo(4));
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

    [Test]
    public async Task The_outcomes_by_score_count_closed_ideas_from_the_highest_score()
    {
        await Weights(("плюс 2", 2));
        await Close(await Add(EntrySide.Long, ["плюс 2"], "AUSDT"), 0.006, 0.6);
        await Close(await Add(EntrySide.Long, ["плюс 2"], "BUSDT"), -0.002, -0.2);
        await Close(await Add(EntrySide.Long, [], "CUSDT"), -0.004, -0.4);
        await Add(EntrySide.Long, ["плюс 2"], "DUSDT");

        IReadOnlyList<IdeaScoreRow> rows = await _ideas.ScoresAsync(Closed(), CancellationToken.None);

        Assert.That(rows.Select(row => (row.Score, row.Ideas)), Is.EqualTo(new[] { (2, 2), (0, 1) }));
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

        Assert.That(all.Select(row => (row.Score, row.Ideas)), Is.EqualTo(new[] { (1, 2) }));
        Assert.That(shorts.Select(row => (row.Score, row.Ideas, row.Mean)), Is.EqualTo(new[] { (1, 1, 0.002) }));
    }

    [Test]
    public async Task After_a_change_of_a_weight_the_outcomes_put_the_ideas_at_their_new_scores()
    {
        await Weights(("плюс 1", 1));
        await Close(await Add(EntrySide.Long, ["плюс 1"], "AUSDT"), 0.001);
        Assert.That((await _ideas.ScoresAsync(Closed(), CancellationToken.None)).Single().Score, Is.EqualTo(1));

        await _weights.SetAsync(new LabelWeight("плюс 1", 3, 0), CancellationToken.None);

        Assert.That((await _ideas.ScoresAsync(Closed(), CancellationToken.None)).Single().Score, Is.EqualTo(3));
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

    private async Task<Dictionary<Guid, int>> Scores(IdeaListKind kind)
    {
        ListPage<Idea> page = await _ideas.ListAsync(new IdeaListQuery(kind, new ListOrder("moment", true, 1, 200)), CancellationToken.None);
        return page.Items.ToDictionary(idea => idea.Id, idea => idea.Score);
    }

    private static MarketPair Pair(string symbol, DateTimeOffset at)
    {
        return new MarketPair(symbol, 1m, 0.01m, 0.02m, null, 1_000_000m, 24_000m, 24_000m, 0.0001m, at.AddHours(2), at);
    }
}
