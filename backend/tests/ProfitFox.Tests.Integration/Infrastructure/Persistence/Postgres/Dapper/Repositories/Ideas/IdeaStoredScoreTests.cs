using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring.Queries;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     Change add-idea-score, tasks 8.2–8.4 (design.md, decision 2): the score is kept in <c>ideas.score</c> — written with a
///     new idea from the weights now, counted again for the ideas of a text whose weights change or move, under the lock of
///     the weights — and always equals <see cref="ListColumns.ScoreOf(string)" />; an idea read by id or among the open ones
///     carries it (spec idea-score «Смена веса», «Переименование», «Итоги после смены веса»).
/// </summary>
[NonParallelizable]
public sealed class IdeaStoredScoreTests
{
    private IIdeaRepository _ideas = null!;
    private ILabelWeightRepository _weights = null!;
    private ISignalLabelRepository _labels = null!;
    private ISignalRepository _signals = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _ideas = BenchServices.Get<IIdeaRepository>();
        _weights = BenchServices.Get<ILabelWeightRepository>();
        _labels = BenchServices.Get<ISignalLabelRepository>();
        _signals = BenchServices.Get<ISignalRepository>();
    }

    [Test]
    public async Task An_idea_added_after_a_change_of_a_weight_is_scored_by_the_new_weight()
    {
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", -2, 1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", 3, 1), CancellationToken.None);

        Idea buy = await Add(EntrySide.Long, ["↓ BTC 4h", "без веса"], "AUSDT");
        Idea sell = await Add(EntrySide.Short, ["↓ BTC 4h"], "BUSDT");

        Assert.That((await Stored(buy.Id), await Stored(sell.Id)), Is.EqualTo((3m, 1m)));
    }

    [Test]
    public async Task An_idea_read_by_id_or_among_the_open_ones_carries_its_score()
    {
        await _weights.SetAsync(new LabelWeight("плюс 2", 2, 0), CancellationToken.None);
        Idea idea = await Add(EntrySide.Long, ["плюс 2"]);

        Assert.That((await _ideas.FindAsync(idea.Id, CancellationToken.None))!.Score, Is.EqualTo(2m));
        Assert.That((await _ideas.OpenAsync(CancellationToken.None)).Single().Score, Is.EqualTo(2m));
    }

    [Test]
    public async Task A_change_of_a_weight_counts_again_the_closed_and_open_ideas_with_that_label_only()
    {
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", -2, 1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("оборот ↑ 1h", 1, 0), CancellationToken.None);
        Idea closed = await Close(await Add(EntrySide.Long, ["↓ BTC 4h", "оборот ↑ 1h", "⚡ всплеск"], "AUSDT"));
        Idea open = await Add(EntrySide.Short, ["↓ BTC 4h"], "BUSDT");
        Idea other = await Add(EntrySide.Long, ["оборот ↑ 1h"], "CUSDT");
        Assert.That(await Stored(closed.Id), Is.EqualTo(-1m));

        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", 0, -3), CancellationToken.None);

        Assert.That((await Stored(closed.Id), await Stored(open.Id), await Stored(other.Id)), Is.EqualTo((1m, -3m, 1m)));
        IReadOnlyList<IdeaScoreRow> rows = await _ideas.ScoresAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("exitTime", true, 1, 50)), CancellationToken.None);
        Assert.That(rows.Select(row => (row.Score, row.Ideas)), Is.EqualTo(new[] { (1, 1) }), "the outcomes put the closed idea at its new score");
    }

    [Test]
    public async Task A_renamed_label_takes_its_weight_to_the_new_text_and_an_idea_with_the_former_text_gets_nought_from_it()
    {
        Guid signal = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-down"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "↓ BTC", TimeSpan.FromHours(4)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC", -2, 0), CancellationToken.None);
        Idea before = await Add(EntrySide.Long, ["↓ BTC"], "AUSDT");
        Idea later = await Add(EntrySide.Long, ["↓ BTC 5m"], "BUSDT");
        Assert.That((await Stored(before.Id), await Stored(later.Id)), Is.EqualTo((-2m, 0m)));

        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "↓ BTC 5m", TimeSpan.FromMinutes(5)), CancellationToken.None);

        Assert.That((await Stored(before.Id), await Stored(later.Id)), Is.EqualTo((0m, -2m)));
    }

    [Test]
    public async Task An_idea_begun_while_a_weight_changes_waits_for_the_change_and_is_scored_by_the_new_weight()
    {
        await _weights.SetAsync(new LabelWeight("плюс", 1, 0), CancellationToken.None);
        await using NpgsqlConnection change = await BenchDatabase.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await change.BeginTransactionAsync();
        await change.ExecuteAsync(IdeaScoreQueries.LockWeights, transaction: transaction);
        await change.ExecuteAsync("UPDATE label_weights SET long_weight = 3 WHERE text = 'плюс'", transaction: transaction);

        Idea idea = IdeaGenerator.Generate(side: EntrySide.Long, labels: ["плюс"]);
        Task<bool> adding = _ideas.AddAsync(idea, CancellationToken.None);
        await Task.Delay(500);
        Assert.That(adding.IsCompleted, Is.False, "the idea waits for the change of the weights");
        await transaction.CommitAsync();

        Assert.That(await adding, Is.True);
        Assert.That(await Stored(idea.Id), Is.EqualTo(3m));
    }

    // The scanner saves the ideas of a pass in one statement; a change of a weight writes the score of many of the same rows.
    // Interleaved, the two take the locks of the rows in different orders and Postgres breaks one off as a deadlock — the
    // scanner then stops. Under the lock of the weights the save waits for the change to end instead.
    [Test]
    public async Task A_save_of_the_scanner_begun_while_a_weight_changes_waits_for_the_change()
    {
        await _weights.SetAsync(new LabelWeight("плюс", 1, 0), CancellationToken.None);
        Idea idea = await Add(EntrySide.Long, ["плюс"]);
        await using NpgsqlConnection change = await BenchDatabase.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await change.BeginTransactionAsync();
        await change.ExecuteAsync(IdeaScoreQueries.LockWeights, transaction: transaction);

        idea.Now = 0.002;
        Task saving = _ideas.SaveAllAsync([idea], CancellationToken.None);
        await Task.Delay(500);
        Assert.That(saving.IsCompleted, Is.False, "the save waits for the change of the weights");
        await transaction.CommitAsync();

        await saving;
        Assert.That((await _ideas.FindAsync(idea.Id, CancellationToken.None))!.Now, Is.EqualTo(0.002));
    }

    [Test]
    public async Task After_any_changes_of_weights_renames_and_new_ideas_the_score_of_every_idea_is_that_of_the_weights_now()
    {
        var random = new Random(20261128);
        string[] texts = ["↓ BTC", "↓ BTC 5m", "оборот ↑", "фандинг ↑", "⚡ всплеск", "OI ↑ 1h"];
        var signals = new Guid[3];
        var labelTexts = new string[3];
        for (int index = 0; index < signals.Length; index++)
        {
            signals[index] = (await _signals.CreateAsync(SignalDocumentGenerator.Generate($"signal-{index}"), CancellationToken.None)).Id;
            labelTexts[index] = texts[index * 2];
            await _labels.SetAsync(new SignalLabel(signals[index], Enabled: false, labelTexts[index], TimeSpan.FromHours(1)), CancellationToken.None);
        }

        for (int step = 0; step < 200; step++)
        {
            switch (random.Next(3))
            {
                case 0:
                    await _weights.SetAsync(new LabelWeight(texts[random.Next(texts.Length)], Hundredths(random), Hundredths(random)), CancellationToken.None);
                    break;
                case 1:
                    int which = random.Next(signals.Length);
                    labelTexts[which] = texts[random.Next(texts.Length)];
                    await _labels.SetAsync(new SignalLabel(signals[which], Enabled: false, labelTexts[which], TimeSpan.FromHours(1)), CancellationToken.None);
                    break;
                default:
                    string[] labels = [.. texts.Where(_ => random.Next(3) == 0)];
                    Idea idea = IdeaGenerator.Generate(
                        side: random.Next(2) == 0 ? EntrySide.Long : EntrySide.Short,
                        symbol: $"P{step}USDT",
                        labels: labels);
                    await _ideas.AddAsync(idea, CancellationToken.None);
                    if (random.Next(2) == 0)
                    {
                        await Close(idea);
                    }

                    break;
            }
        }

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        (Guid Id, decimal Stored, decimal Counted)[] ideas = [.. await connection.QueryAsync<(Guid, decimal, decimal)>($"SELECT i.id, i.score, {ListColumns.ScoreOf("i")} FROM ideas i")];
        Assert.That(ideas, Has.Length.GreaterThan(30));
        Assert.That(ideas.Where(idea => idea.Stored != idea.Counted), Is.Empty);
        Assert.That(ideas.Select(idea => idea.Stored).Distinct().Count(), Is.GreaterThan(3), "the scores are not all alike");
        Assert.That(ideas.Any(idea => idea.Stored != decimal.Truncate(idea.Stored)), Is.True, "some scores have hundredths");
    }

    // A weight of −3 to +3 in hundredths (change decimal-label-weights, task 2.4).
    private static decimal Hundredths(Random random)
    {
        return random.Next(-300, 301) / 100m;
    }

    private async Task<Idea> Add(EntrySide side, string[] labels, string symbol = "BTCUSDT")
    {
        Idea idea = IdeaGenerator.Generate(symbol: symbol, side: side, labels: labels);
        await _ideas.AddAsync(idea, CancellationToken.None);
        return idea;
    }

    private async Task<Idea> Close(Idea idea)
    {
        idea.EntryPrice = 100;
        idea.EntryTime = idea.Moment;
        idea.State = IdeaState.Closed;
        idea.ExitTime = DateTimeOffset.UtcNow.AddHours(-1);
        idea.ExitReason = ExitReason.Time;
        idea.Result = 0.001;
        idea.ResultUsdt = 0.1;
        await _ideas.SaveAsync(idea, CancellationToken.None);
        return idea;
    }

    private static async Task<decimal> Stored(Guid id)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<decimal>("SELECT score FROM ideas WHERE id = @Id", new { Id = id });
    }
}
