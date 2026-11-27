using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring;

/// <summary>
///     Change add-idea-score, tasks 2.2 and 2.4 (design.md, decision 1): the weights of the labels are read, set by text and
///     moved to a new text; a label whose text changes takes its weights along in the same transaction, and a label turned
///     off and on keeps them (spec idea-score «Вес при смене текста метки»).
/// </summary>
[NonParallelizable]
public sealed class LabelWeightRepositoryTests
{
    private ILabelWeightRepository _weights = null!;
    private ISignalLabelRepository _labels = null!;
    private ISignalRepository _signals = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _weights = BenchServices.Get<ILabelWeightRepository>();
        _labels = BenchServices.Get<ISignalLabelRepository>();
        _signals = BenchServices.Get<ISignalRepository>();
    }

    [Test]
    public async Task Weights_are_set_by_text_and_replaced()
    {
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", -2, 1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("⚡ всплеск", 0, -1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", 0, 1), CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ BTC 4h", 0, 1), new LabelWeight("⚡ всплеск", 0, -1) }));
    }

    [Test]
    public async Task Weights_move_to_a_new_text()
    {
        await _weights.SetAsync(new LabelWeight("↓ BTC", -2, 1), CancellationToken.None);

        await _weights.MoveAsync("↓ BTC", "↓ BTC 5m", CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ BTC 5m", -2, 1) }));
    }

    [Test]
    public async Task A_new_text_with_weights_of_its_own_keeps_them()
    {
        await _weights.SetAsync(new LabelWeight("↓ BTC", -2, 1), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC 5m", 3, 3), CancellationToken.None);

        await _weights.MoveAsync("↓ BTC", "↓ BTC 5m", CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ BTC 5m", 3, 3) }));
    }

    [Test]
    public async Task A_renamed_label_takes_its_weights_to_the_new_text()
    {
        Guid signal = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-down"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "↓ BTC", TimeSpan.FromHours(4)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC", -2, 0), CancellationToken.None);

        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "↓ BTC 5m", TimeSpan.FromMinutes(5)), CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ BTC 5m", -2, 0) }), "the former text weighs 0");
    }

    [Test]
    public async Task A_label_turned_off_and_on_with_the_same_text_keeps_its_weights()
    {
        Guid signal = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("funding-hot"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "фандинг ↑", TimeSpan.FromHours(1)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("фандинг ↑", 0, 2), CancellationToken.None);

        await _labels.SetAsync(new SignalLabel(signal, Enabled: false, "фандинг ↑", TimeSpan.FromHours(1)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(signal, Enabled: true, "фандинг ↑", TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("фандинг ↑", 0, 2) }));
    }

    [Test]
    public async Task A_text_another_label_still_has_keeps_its_weights_when_one_label_is_renamed()
    {
        Guid first = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("first"), CancellationToken.None)).Id;
        Guid second = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("second"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(first, Enabled: false, "↓ пара", TimeSpan.FromHours(1)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(second, Enabled: true, "↓ пара", TimeSpan.FromHours(1)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ пара", 1, -1), CancellationToken.None);

        await _labels.SetAsync(new SignalLabel(first, Enabled: false, "↓ пара 1h", TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ пара", 1, -1), new LabelWeight("↓ пара 1h", 1, -1) }));
    }

    [Test]
    public async Task A_label_refused_for_a_taken_text_moves_no_weights()
    {
        Guid first = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("first"), CancellationToken.None)).Id;
        Guid second = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("second"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(first, Enabled: true, "↓ BTC", TimeSpan.FromHours(1)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(second, Enabled: true, "↑ BTC", TimeSpan.FromHours(1)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↑ BTC", 2, 0), CancellationToken.None);

        Assert.That(await _labels.SetAsync(new SignalLabel(second, Enabled: true, "↓ BTC", TimeSpan.FromHours(1)), CancellationToken.None), Is.False);

        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↑ BTC", 2, 0) }));
    }

    [Test]
    public async Task The_labels_of_the_page_are_a_row_a_text_with_the_label_that_is_on_and_their_weights()
    {
        Guid hot = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("funding-hot"), CancellationToken.None)).Id;
        Guid down = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-down-4h"), CancellationToken.None)).Id;
        Guid pair = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("pair-down"), CancellationToken.None)).Id;
        Guid old = (await _signals.CreateAsync(SignalDocumentGenerator.Generate("a-old-down"), CancellationToken.None)).Id;
        await _labels.SetAsync(new SignalLabel(hot, Enabled: true, "фандинг ↑", TimeSpan.FromHours(1)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(down, Enabled: true, "↓ BTC 4h", TimeSpan.FromHours(4)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(old, Enabled: false, "↓ BTC 4h", TimeSpan.FromHours(1)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(pair, Enabled: false, "↓ пара", TimeSpan.FromHours(1)), CancellationToken.None);
        await _weights.SetAsync(new LabelWeight("↓ BTC 4h", -2, 1), CancellationToken.None);

        Assert.That(await _weights.LabelsAsync(CancellationToken.None), Is.EqualTo(new[]
        {
            new ScoringLabel("↓ BTC 4h", true, "4h", "btc-down-4h", -2, 1),
            new ScoringLabel("фандинг ↑", true, "1h", "funding-hot", 0, 0),
            new ScoringLabel("↓ пара", false, "1h", "pair-down", 0, 0)
        }));
    }
}
