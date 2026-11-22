using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Runs.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 6.2: the backtests on the database — spec bench-run-queue «Порядок очереди»,
///     «Аренда прогона», «Прерванный прогон», «Отмена прогона» and «Результат прогона», and the last backtest of a strategy.
/// </summary>
[NonParallelizable]
public sealed class BacktestRepositoryTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private readonly CancellationToken _none = CancellationToken.None;
    private IBacktestRepository _backtests = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _backtests = BenchServices.Get<IBacktestRepository>();
    }

    [Test]
    public async Task A_queued_backtest_keeps_its_strategy_graph_settings_and_sets()
    {
        Backtest asked = BacktestGenerator.Generate(thresholds: ["0.0003", "0.0005", "0.001"], timeframes: [TimeSpan.FromHours(1), TimeSpan.FromHours(4)]);

        Backtest queued = await _backtests.EnqueueAsync(asked, _none);
        Backtest read = (await _backtests.FindAsync(asked.Id, _none))!;

        Assert.That((read.Status, read.StrategyName, read.StrategyVersion, read.Kind, read.SetCount), Is.EqualTo((BacktestState.Queued, "funding-short", 3, asked.Kind, 6)));
        Assert.That(read.QueuedAt, Is.EqualTo(queued.QueuedAt));
        Assert.That(read.Graph.Nodes.Select(node => node.Id), Is.EqualTo(asked.Graph.Nodes.Select(node => node.Id)));
        Assert.That(read.Settings.Values.Single().Values, Is.EqualTo(new[] { "0.0003", "0.0005", "0.001" }));
        Assert.That((read.Settings.Start, read.Settings.End, read.Settings.Pairs, read.Settings.TakerFee), Is.EqualTo((asked.Settings.Start, asked.Settings.End, (IReadOnlyList<string>?)null, 0.00055)));
        Assert.That(read.Sets.Select(set => (set.Set.Number, set.Set.Timeframe, set.Set.Values.Single().Value, set.Numbers)), Is.EqualTo(
            asked.Sets.Select(set => (set.Set.Number, set.Set.Timeframe, set.Set.Values.Single().Value, (SetSummary?)null))));
    }

    [Test]
    public async Task Three_backtests_go_in_the_order_of_the_queue_and_one_waits()
    {
        Backtest a = await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "a"), _none);
        Backtest b = await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "b"), _none);
        await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "c"), _none);

        Backtest first = (await _backtests.ClaimAsync("mac-1", Lease, _none))!;
        Backtest second = (await _backtests.ClaimAsync("mac-2", Lease, _none))!;

        Assert.That((first.Id, first.LeaseOwner, second.Id, second.LeaseOwner), Is.EqualTo((a.Id, (string?)"mac-1", b.Id, (string?)"mac-2")));
        Assert.That((await _backtests.ActiveAsync(_none)).Select(backtest => (backtest.Id, backtest.Status)), Is.EqualTo(new[]
        {
            ("a", BacktestState.Running), ("b", BacktestState.Running), ("c", BacktestState.Queued)
        }));
        Assert.That(first.Sets, Has.Count.EqualTo(1), "the Worker gets the sets with the backtest");
    }

    [Test]
    public async Task A_backtest_whose_lease_expired_starts_again_at_any_Worker_from_the_first_pair()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        await _backtests.RenewAsync(backtest.Id, "mac-1", Lease, pairsDone: 300, pairsTotal: 828, _none);
        Assert.That(await _backtests.ClaimAsync("mac-2", Lease, _none), Is.Null, "the lease holds");

        await Expire(backtest.Id);
        Backtest again = (await _backtests.ClaimAsync("mac-2", Lease, _none))!;

        Assert.That((again.Id, again.LeaseOwner, again.PairsDone, again.PairsTotal, again.LostLeases), Is.EqualTo((backtest.Id, (string?)"mac-2", 0, (int?)null, 1)));
        Assert.That(await _backtests.RenewAsync(backtest.Id, "mac-1", Lease, 301, 828, _none), Is.Null, "the first Worker no longer holds it");
        Assert.That(
            () => _backtests.FinishAsync(new RunAssignment(backtest.Id, "mac-1"), Outcome(1), _none),
            Throws.InstanceOf<LeaseLostException>(),
            "and writes nothing");
        Assert.That((await _backtests.FindAsync(backtest.Id, _none))!.Status, Is.EqualTo(BacktestState.Running));
    }

    [Test]
    public async Task A_backtest_interrupted_once_and_finished_by_another_Worker_has_one_result()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        await Expire(backtest.Id);
        await _backtests.ClaimAsync("mac-2", Lease, _none);

        await _backtests.FinishAsync(new RunAssignment(backtest.Id, "mac-2"), Outcome(1), _none);

        Backtest done = (await _backtests.FindAsync(backtest.Id, _none))!;
        Assert.That((done.Status, done.LostLeases), Is.EqualTo((BacktestState.Done, 1)));
        Assert.That(await Count("backtest_set_pairs"), Is.EqualTo(2));
    }

    [Test]
    public async Task A_backtest_that_lost_its_lease_three_times_ends_failed_and_is_not_taken_again()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none);
        Backtest? taken = await _backtests.ClaimAsync("mac-1", Lease, _none);
        for (int lost = 0; lost < 3; lost++)
        {
            await Expire(backtest.Id);
            taken = await _backtests.ClaimAsync($"mac-{lost + 2}", Lease, _none);
        }

        Assert.That(taken!.InterruptedTooOften, Is.True);
        await _backtests.StopAsync(new RunAssignment(backtest.Id, taken.LeaseOwner!), BacktestState.Failed, "прерван 3 раза", _none);

        Backtest failed = (await _backtests.FindAsync(backtest.Id, _none))!;
        Assert.That((failed.Status, failed.Reason, failed.FinishedAt is not null), Is.EqualTo((BacktestState.Failed, (string?)"прерван 3 раза", true)));
        Assert.That(failed.Sets.Select(set => set.Numbers), Has.All.Null);
        await Expire(backtest.Id);
        Assert.That(await _backtests.ClaimAsync("mac-9", Lease, _none), Is.Null);
    }

    [Test]
    public async Task A_waiting_backtest_cancelled_is_gone()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none);

        CancelOutcome outcome = await _backtests.CancelAsync(backtest.Id, _none);

        Assert.That(outcome, Is.EqualTo(CancelOutcome.Removed));
        Assert.That(await _backtests.FindAsync(backtest.Id, _none), Is.Null);
        Assert.That(await _backtests.ActiveAsync(_none), Is.Empty);
        Assert.That(await _backtests.FinishedAsync(20, _none), Is.Empty);
        Assert.That(await Count("backtest_sets"), Is.Zero);
    }

    [Test]
    public async Task A_going_backtest_cancelled_stops_cancelled_and_the_same_can_be_queued_again()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);

        CancelOutcome outcome = await _backtests.CancelAsync(backtest.Id, _none);
        Renewal? renewal = await _backtests.RenewAsync(backtest.Id, "mac-1", Lease, 300, 828, _none);
        await _backtests.StopAsync(new RunAssignment(backtest.Id, "mac-1"), BacktestState.Cancelled, "отменён: пройдено 300 пар из 828", _none);

        Assert.That((outcome, renewal!.CancelRequested), Is.EqualTo((CancelOutcome.Requested, true)));
        Backtest cancelled = (await _backtests.FindAsync(backtest.Id, _none))!;
        Assert.That((cancelled.Status, cancelled.Reason), Is.EqualTo((BacktestState.Cancelled, (string?)"отменён: пройдено 300 пар из 828")));
        Assert.That(await _backtests.CancelAsync(backtest.Id, _none), Is.EqualTo(CancelOutcome.AlreadyFinished));
        Assert.That(await _backtests.CancelAsync("none", _none), Is.EqualTo(CancelOutcome.NotFound));
        Assert.That((await _backtests.EnqueueAsync(BacktestGenerator.Generate(), _none)).Status, Is.EqualTo(BacktestState.Queued));
    }

    [Test]
    public async Task A_done_backtest_has_the_numbers_of_each_set_and_its_pairs_with_its_end()
    {
        Backtest backtest = await _backtests.EnqueueAsync(BacktestGenerator.Generate(thresholds: ["0.0003", "0.0005", "0.001"], timeframes: [TimeSpan.FromHours(1), TimeSpan.FromHours(4)]), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        Assert.That((await _backtests.FindAsync(backtest.Id, _none))!.Sets.Select(set => set.Numbers), Has.All.Null, "a going backtest has no numbers");

        await _backtests.FinishAsync(new RunAssignment(backtest.Id, "mac-1"), Outcome(6), _none);

        Backtest done = (await _backtests.FindAsync(backtest.Id, _none))!;
        Assert.That((done.Status, done.EngineVersion, done.DataHash, done.ReadSeconds, done.ComputeSeconds), Is.EqualTo((BacktestState.Done, (int?)2, (string?)"hash", (double?)12.5, (double?)1.5)));
        Assert.That(done.Sets.Select(set => set.Numbers!.Amount), Is.EqualTo(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }));
        Assert.That(done.Sets[0].Numbers, Is.EqualTo(Numbers(1)));
        Assert.That((await _backtests.PairsAsync(backtest.Id, 2, _none))!.Select(pair => (pair.Pair, pair.Numbers.Amount)), Is.EqualTo(new[] { ("BTCUSDT", 2.0), ("ETHUSDT", -0.5) }));
        Assert.That(await _backtests.PairsAsync(backtest.Id, 7, _none), Is.Null);
        Backtest finished = (await _backtests.FinishedAsync(20, _none)).Single();
        Assert.That((finished.Id, finished.BestAmount, finished.SetCount), Is.EqualTo((backtest.Id, (double?)6.0, 6)));
    }

    [Test]
    public async Task The_last_backtest_of_a_strategy_is_the_first_waiting_one_or_the_last_ended()
    {
        var fundingShort = Guid.NewGuid();
        var ladder = Guid.NewGuid();
        Backtest ended = await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "1", strategy: fundingShort), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        await _backtests.FinishAsync(new RunAssignment(ended.Id, "mac-1"), Outcome(1), _none);
        await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "2", strategy: ladder, name: "ladder"), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        await _backtests.StopAsync(new RunAssignment("2", "mac-1"), BacktestState.Failed, "история недоступна", _none);
        await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "3", strategy: ladder, name: "ladder"), _none);
        await _backtests.ClaimAsync("mac-1", Lease, _none);
        await _backtests.FinishAsync(new RunAssignment("3", "mac-1"), Outcome(1), _none);

        IReadOnlyDictionary<Guid, Backtest> last = await _backtests.LastOfStrategiesAsync(_none);
        Assert.That((last[fundingShort].Id, last[ladder].Id), Is.EqualTo(("1", "3")));

        await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "4", strategy: fundingShort), _none);
        await _backtests.EnqueueAsync(BacktestGenerator.Generate(id: "5", strategy: fundingShort), _none);

        last = await _backtests.LastOfStrategiesAsync(_none);
        Assert.That((last[fundingShort].Id, last[fundingShort].Status), Is.EqualTo(("4", BacktestState.Queued)), "a waiting backtest comes before an ended one");
    }

    private static BacktestOutcome Outcome(int sets)
    {
        return new BacktestOutcome(
            [.. Enumerable.Range(1, sets).Select(set => (set, Numbers(set), (IReadOnlyList<PairSummary>)[new PairSummary("ETHUSDT", Numbers(-0.5)), new PairSummary("BTCUSDT", Numbers(set))]))],
            EngineVersion: 2,
            DataHash: "hash",
            ReadSeconds: 12.5,
            ComputeSeconds: 1.5,
            Pairs: 828);
    }

    private static SetSummary Numbers(double amount)
    {
        return new SetSummary(3, 2, 1, 0.004, amount, 0.5, new Exclusions(Coverage: 1), Skipped: 0, OpenCycles: 0, OpenAmount: 0, new RandomDifference(0.001, 2, T: 1.5));
    }

    private static async Task Expire(string id)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE backtests SET leased_until = now() - interval '1 second' WHERE id = @Id", new { Id = id });
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }
}
