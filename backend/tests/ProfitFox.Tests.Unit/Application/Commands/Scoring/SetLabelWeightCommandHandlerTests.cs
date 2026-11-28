using System.Globalization;

using ProfitFox.Application.Commands.Scoring.SetLabelWeight;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Scoring;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Tests.Unit.Application.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Commands.Scoring;

/// <summary>
///     Change add-idea-score, task 2.3, and change decimal-label-weights, task 2.2: spec idea-score «Вес метки» — the weights of a label and of «⚡ всплеск», the default
///     of 0, and a weight out of range or of a text there is no label with refused without a change.
/// </summary>
public sealed class SetLabelWeightCommandHandlerTests
{
    private static readonly Guid BtcDown = Guid.NewGuid();
    private static readonly Guid Turnover = Guid.NewGuid();
    private MemorySignalLabelRepository _labels = null!;
    private MemoryLabelWeightRepository _weights = null!;

    [SetUp]
    public async Task Labels()
    {
        _labels = new MemorySignalLabelRepository();
        _weights = new MemoryLabelWeightRepository(_labels, new Dictionary<Guid, string> { [BtcDown] = "btc-down-4h", [Turnover] = "turnover-high-1h" });
        await _labels.SetAsync(new SignalLabel(BtcDown, true, "↓ BTC 4h", TimeSpan.FromHours(4)), CancellationToken.None);
        await _labels.SetAsync(new SignalLabel(Turnover, true, "оборот ↑ 1h", TimeSpan.FromHours(1)), CancellationToken.None);
    }

    [Test]
    public async Task A_label_whose_weights_were_never_set_weighs_nought_on_either_side()
    {
        IReadOnlyList<ScoringLabel> rows = await new GetScoringLabelsQueryHandler(_weights).Handle(new GetScoringLabelsQuery(), CancellationToken.None);

        Assert.That(rows.Single(row => row.Text == "оборот ↑ 1h"), Is.EqualTo(new ScoringLabel("оборот ↑ 1h", true, "1h", "turnover-high-1h", 0, 0)));
    }

    [Test]
    public async Task The_weights_of_the_sides_are_saved_and_the_list_gives_them()
    {
        ScoringLabel row = await Handle(new SetLabelWeightCommand("↓ BTC 4h", -2, 1));

        Assert.That(row, Is.EqualTo(new ScoringLabel("↓ BTC 4h", true, "4h", "btc-down-4h", -2, 1)));
        IReadOnlyList<ScoringLabel> rows = await new GetScoringLabelsQueryHandler(_weights).Handle(new GetScoringLabelsQuery(), CancellationToken.None);
        Assert.That(rows.Single(label => label.Text == "↓ BTC 4h"), Is.EqualTo(row));
    }

    // Change decimal-label-weights, task 2.2 (design.md, decision 2): spec idea-score «Вес вне диапазона».
    [TestCase("101", "0", "longWeight")]
    [TestCase("-100.01", "0", "longWeight")]
    [TestCase("0", "150", "shortWeight")]
    public async Task A_weight_out_of_range_is_refused_and_the_weights_stay(string longWeight, string shortWeight, string field)
    {
        await Handle(new SetLabelWeightCommand("↓ BTC 4h", -2, 1));

        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetLabelWeightCommand("↓ BTC 4h", decimal.Parse(longWeight, CultureInfo.InvariantCulture), decimal.Parse(shortWeight, CultureInfo.InvariantCulture))))!;

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field, error.Reason)), Is.EqualTo(new[] { ("InvalidWeight", (string?)field, "вес от −100 до +100, не больше двух знаков после точки") }));
        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("↓ BTC 4h", -2, 1) }));
    }

    // Change decimal-label-weights, task 2.2: spec idea-score «Дробный вес»; the bounds are weights too.
    [Test]
    public async Task A_weight_with_hundredths_is_saved_and_the_list_gives_it()
    {
        ScoringLabel row = await Handle(new SetLabelWeightCommand("↓ BTC 4h", -100, 0.25m));
        await Handle(new SetLabelWeightCommand("оборот ↑ 1h", 100, -2.45m));

        Assert.That(row, Is.EqualTo(new ScoringLabel("↓ BTC 4h", true, "4h", "btc-down-4h", -100, 0.25m)));
        IReadOnlyList<ScoringLabel> rows = await new GetScoringLabelsQueryHandler(_weights).Handle(new GetScoringLabelsQuery(), CancellationToken.None);
        Assert.That(rows.Select(label => (label.Text, label.LongWeight, label.ShortWeight)), Is.EquivalentTo(new[] { ("↓ BTC 4h", -100m, 0.25m), ("оборот ↑ 1h", 100m, -2.45m), ("⚡ всплеск", 0m, 0m) }));
    }

    // Change decimal-label-weights, task 2.2: spec idea-score «Вес дальше сотых»; a nought after the hundredths is no digit.
    [Test]
    public async Task A_weight_with_digits_past_the_hundredths_is_refused_and_one_with_noughts_there_is_not()
    {
        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetLabelWeightCommand("↓ BTC 4h", 0.125m, 0)))!;

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { ("InvalidWeight", (string?)"longWeight") }));
        Assert.That(_weights.Writes, Is.Zero);
        Assert.That((await Handle(new SetLabelWeightCommand("↓ BTC 4h", 0.100m, 0))).LongWeight, Is.EqualTo(0.1m));
    }

    [Test]
    public void A_missing_weight_is_refused_at_its_field()
    {
        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetLabelWeightCommand("↓ BTC 4h", null, null)))!;

        Assert.That(refused.Errors.Select(error => error.Field), Is.EqualTo(new[] { "longWeight", "shortWeight" }));
    }

    [Test]
    public async Task The_built_in_label_takes_a_weight()
    {
        ScoringLabel row = await Handle(new SetLabelWeightCommand("⚡ всплеск", 0, -1));

        Assert.That(row, Is.EqualTo(new ScoringLabel("⚡ всплеск", true, null, null, 0, -1, BuiltIn: true)));
        Assert.That(await _weights.AllAsync(CancellationToken.None), Is.EqualTo(new[] { new LabelWeight("⚡ всплеск", 0, -1) }));
    }

    [Test]
    public void A_text_no_label_has_is_refused()
    {
        LabelNotFoundException refused = Assert.ThrowsAsync<LabelNotFoundException>(() => Handle(new SetLabelWeightCommand("нет такой", 1, 1)))!;

        Assert.That(refused.Text, Is.EqualTo("нет такой"));
        Assert.That(_weights.Writes, Is.Zero);
    }

    [Test]
    public async Task A_label_that_is_off_takes_a_weight()
    {
        var pair = Guid.NewGuid();
        await _labels.SetAsync(new SignalLabel(pair, false, "↓ пара", TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That((await Handle(new SetLabelWeightCommand("↓ пара", 1, 0))).Enabled, Is.False);
    }

    [Test]
    public async Task The_built_in_label_is_the_last_row_and_the_only_one_without_labels()
    {
        var empty = new MemoryLabelWeightRepository(new MemorySignalLabelRepository());

        IReadOnlyList<ScoringLabel> rows = await new GetScoringLabelsQueryHandler(empty).Handle(new GetScoringLabelsQuery(), CancellationToken.None);
        IReadOnlyList<ScoringLabel> all = await new GetScoringLabelsQueryHandler(_weights).Handle(new GetScoringLabelsQuery(), CancellationToken.None);

        Assert.That(rows, Is.EqualTo(new[] { new ScoringLabel("⚡ всплеск", true, null, null, 0, 0, BuiltIn: true) }));
        Assert.That(all.Select(row => row.Text), Is.EquivalentTo(new[] { "↓ BTC 4h", "оборот ↑ 1h", "⚡ всплеск" }));
        Assert.That(all[^1].BuiltIn, Is.True);
    }

    private Task<ScoringLabel> Handle(SetLabelWeightCommand command)
    {
        return new SetLabelWeightCommandHandler(_weights, _labels).Handle(command, CancellationToken.None);
    }
}
