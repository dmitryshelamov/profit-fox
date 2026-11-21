using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Commands.Hypotheses.AddHypothesis;
using ProfitFox.Application.Commands.Runs.EnqueueRun;
using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices;

/// <summary>
///     Spec hypothesis-bench «Сверка до вердикта» through the queue: a parity run a Worker computes gives the same events, the
///     same power and the same hash of the data as a parity run made directly on the same history.
/// </summary>
[NonParallelizable]
public sealed class ParityThroughWorkerTests
{
    [SetUp]
    public async Task CoverFourWeeks()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
    }

    [TearDown]
    public void Uncover()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_parity_run_of_a_Worker_gives_the_events_the_power_and_the_data_hash_of_a_direct_parity_run()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var ledger = new LedgerRepository(BenchDatabase.DataSource);
        await using ServiceProvider services = BenchServices.Start(server);
        ISender sender = services.GetRequiredService<ISender>();
        await sender.Send(new AddHypothesisCommand(BenchRepository.H0001Text()), CancellationToken.None);
        ExecuteRunResult direct = await sender.Send(
            new ExecuteRunCommand("H0001", Version: null, Period.Exploration, Parity: true, new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        await using WorkerHost worker = await WorkerHost.Start(server, "w-1");
        RunInternal queued = await sender.Send(new EnqueueRunCommand("H0001", Version: null, Period.Exploration, Parity: true), CancellationToken.None);
        await Poll.Until("the parity run", async () => (await ledger.EntriesAsync(CancellationToken.None)).Any(line => line.RunId == queued.Id));

        LedgerEntry fromWorker = (await ledger.EntriesAsync(CancellationToken.None)).Single(line => line.RunId == queued.Id);
        Assert.That(fromWorker.Status, Is.EqualTo(RunStatus.Parity));
        Assert.That(fromWorker.DataHash, Is.EqualTo(direct.Entry.DataHash).And.Length.EqualTo(64));
        Assert.That(
            (fromWorker.Power, fromWorker.RequiredT, fromWorker.Blocks, fromWorker.Events, fromWorker.Attempts, fromWorker.Reason, fromWorker.Hash, fromWorker.Version),
            Is.EqualTo((direct.Entry.Power, direct.Entry.RequiredT, direct.Entry.Blocks, direct.Entry.Events, direct.Entry.Attempts, direct.Entry.Reason, direct.Entry.Hash, direct.Entry.Version)));
        Assert.That(direct.Entry.Events, Is.GreaterThan(50), "there are events to compare");
        Assert.That(await StableReportOf(queued.Id), Is.EqualTo(Stable(direct.Report)), "the report is the same but for the run id, its time and the seconds it took");
    }

    private static async Task<string> StableReportOf(string runId)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return Stable(await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { RunId = runId }) ?? "");
    }

    private static string Stable(string report)
    {
        return string.Join('\n', report.Split('\n').Where(line => !line.StartsWith("| Прогон |", StringComparison.Ordinal) && !line.StartsWith("| Чтение истории", StringComparison.Ordinal)));
    }
}
