using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;

/// <summary>
///     Spec signal-constructor «Сигнал, на который ссылается стратегия» at the level of the repositories (change
///     add-strategy-constructor, task 3.3): a signal a version of a saved strategy points at is not deleted, and the database
///     keeps it so also when the delete and the save of the strategy come together.
/// </summary>
public sealed class SignalInUseTests
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
    public async Task Deleting_a_signal_in_use()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);

        SignalLibraryException refusal = Assert.ThrowsAsync<SignalLibraryException>(() => _signals.DeleteAsync("funding-high", _none))!;

        Assert.That(refusal.Problem, Is.EqualTo(SignalProblem.InUse));
        Assert.That(refusal.Strategies, Is.EqualTo(new[] { "funding-short" }));
        Assert.That((await _signals.ReadAsync("funding-high", _none))?.Signal?.Id, Is.EqualTo(signal.Id));
    }

    [Test]
    public async Task A_reference_only_in_an_earlier_version_of_the_strategy()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        SignalLibraryException refusal = Assert.ThrowsAsync<SignalLibraryException>(() => _signals.DeleteAsync("funding-high", _none))!;

        Assert.That(refusal.Strategies, Is.EqualTo(new[] { "funding-short" }));
        Assert.That(await _signals.ExistsAsync("funding-high", _none), Is.True);
    }

    [Test]
    public async Task Deleting_after_the_strategy_is_deleted()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
        await _strategies.DeleteAsync("funding-short", _none);

        await _signals.DeleteAsync("funding-high", _none);

        Assert.That(await _signals.NamesAsync(_none), Is.Empty);
    }

    [Test]
    public async Task The_strategies_in_the_refusal_are_all_of_them_in_name_order()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id, name: "zeta"), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id, name: "alpha"), _none);

        SignalLibraryException refusal = Assert.ThrowsAsync<SignalLibraryException>(() => _signals.DeleteAsync("funding-high", _none))!;

        Assert.That(refusal.Strategies, Is.EqualTo(new[] { "alpha", "zeta" }));
        Assert.That(await _strategies.UsingSignalAsync(signal.Id, _none), Is.EqualTo(new[] { "alpha", "zeta" }));
    }

    [Test]
    public async Task A_delete_together_with_a_save_of_a_strategy_never_leaves_a_reference_to_a_deleted_signal()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await BenchDatabase.Reset();
            SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);

            Task delete = _signals.DeleteAsync("funding-high", _none);
            Task save = _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
            await Settle(delete);
            await Settle(save);

            bool signalKept = await _signals.ExistsAsync("funding-high", _none);
            bool strategyKept = await _strategies.ExistsAsync("funding-short", _none);
            // Either the save came first and the delete was refused, or the delete came first and the save was refused.
            Assert.That(strategyKept, Is.EqualTo(signalKept), $"attempt {attempt}: exactly one of the two went through");
            Assert.That(await Dangling(), Is.Zero);
        }
    }

    // The refusal one of the two may get is the point of the test; anything else is a failure.
    private static async Task Settle(Task task)
    {
        try
        {
            await task;
        }
        catch (SignalLibraryException exception) when (exception.Problem == SignalProblem.InUse)
        {
        }
        catch (StrategyLibraryException exception) when (exception.Problem == StrategyProblem.Invalid)
        {
        }
    }

    private static async Task<int> Dangling()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM strategy_signals x WHERE NOT EXISTS (SELECT 1 FROM signals s WHERE s.id = x.signal_id)");
    }
}
