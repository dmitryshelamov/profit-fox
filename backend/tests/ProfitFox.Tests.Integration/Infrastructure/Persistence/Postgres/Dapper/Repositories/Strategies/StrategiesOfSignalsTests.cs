using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>
///     Spec signal-constructor «Стратегии сигнала в списке» at the level of the repository (change redesign-bench-ui, task
///     2.1): the strategies of every signal by one read — the same strategies the refusal of a delete names.
/// </summary>
public sealed class StrategiesOfSignalsTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private IStrategyRepository _strategies = null!;
    private ISignalRepository _signals = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _strategies = BenchServices.Get<IStrategyRepository>();
        _signals = BenchServices.Get<ISignalRepository>();
    }

    [Test]
    public async Task A_strategy_that_pointed_at_the_signal_only_in_its_version_1_is_still_named()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        IReadOnlyDictionary<Guid, IReadOnlyList<string>> used = await _strategies.UsingSignalsAsync(_none);

        Assert.That(used.Keys, Is.EqualTo(new[] { signal.Id }));
        Assert.That(used[signal.Id], Is.EqualTo(new[] { "funding-short" }));
        Assert.That(used[signal.Id], Is.EqualTo(await _strategies.UsingSignalAsync(signal.Id, _none)), "the strategies the refusal of a delete names");
    }

    [Test]
    public async Task A_signal_without_references_is_not_in_the_answer()
    {
        SignalDocument used = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        SignalDocument free = await _signals.CreateAsync(SignalDocumentGenerator.Generate("candle-drop"), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(used.Id), _none);

        IReadOnlyDictionary<Guid, IReadOnlyList<string>> answer = await _strategies.UsingSignalsAsync(_none);

        Assert.That(answer.ContainsKey(free.Id), Is.False);
        Assert.That(answer.Keys, Is.EqualTo(new[] { used.Id }));
    }

    [Test]
    public async Task The_names_of_each_signal_are_in_name_order_and_each_strategy_is_named_once()
    {
        SignalDocument funding = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        SignalDocument candle = await _signals.CreateAsync(SignalDocumentGenerator.Generate("candle-drop"), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(funding.Id, name: "zeta"), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(funding.Id, name: "alpha"), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(candle.Id, name: "middle"), _none);
        // Two versions of one strategy point at the same signal: its name is there once.
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(funding.Id, name: "alpha", graph: StrategyDocumentGenerator.GenerateGraph(funding.Id, after: "8h")), _none);

        IReadOnlyDictionary<Guid, IReadOnlyList<string>> used = await _strategies.UsingSignalsAsync(_none);

        Assert.That(used[funding.Id], Is.EqualTo(new[] { "alpha", "zeta" }));
        Assert.That(used[candle.Id], Is.EqualTo(new[] { "middle" }));
    }

    [Test]
    public async Task Without_strategies_the_answer_is_empty()
    {
        await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);

        Assert.That(await _strategies.UsingSignalsAsync(_none), Is.Empty);
    }
}
