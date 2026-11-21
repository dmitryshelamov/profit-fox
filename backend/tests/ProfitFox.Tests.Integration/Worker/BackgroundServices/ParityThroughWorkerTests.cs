using Dapper;

using Npgsql;

using ProfitFox.Application.Commands.Hypotheses;
using ProfitFox.Application.Commands.Runs;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Ports.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs;
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
        var ledger = new RunLedger(BenchDatabase.DataSource);
        var hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));
        await hypotheses.Add(BenchRepository.H0001Text(), CancellationToken.None);
        var runner = new HypothesisRunner(hypotheses, new GrpcHistorySource(server.Channel), new InstrumentCatalog(server.Http), ledger, TextWriter.Null);
        RunResult direct = await runner.Run(new RunOptions("H0001", Version: null, Period.Exploration, Parity: true, new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        await using WorkerHost worker = await WorkerHost.Start(server, "w-1");
        RunRecord queued = await new RunScheduler(hypotheses, ledger, new RunQueue(BenchDatabase.DataSource)).Enqueue("H0001", version: null, Period.Exploration, parity: true, CancellationToken.None);
        await Poll.Until("the parity run", async () => (await ledger.Entries(CancellationToken.None)).Any(line => line.RunId == queued.Id));

        LedgerEntry fromWorker = (await ledger.Entries(CancellationToken.None)).Single(line => line.RunId == queued.Id);
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
