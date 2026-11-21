using Dapper;
using Npgsql;
using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Verdicts;
using ProfitFox.Infrastructure.Hypotheses;
using ProfitFox.Infrastructure.Ledger;
using ProfitFox.Infrastructure.MarketData;
using ProfitFox.Domain.Trades;
using ProfitFox.Tests.Integration.Bench.Database;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench end to end: H0001 on four weeks of history of a fake market-data service, over the bench
///     database, gives a line in the ledger, a report and a verdict; a parity run never computes the outcomes of events; a
///     refusal of the ledger comes before any history is read.
/// </summary>
[NonParallelizable]
public sealed class HypothesisRunTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private readonly HypothesisCatalog _hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));
    private readonly RunLedger _ledger = new RunLedger(BenchDatabase.DataSource);

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        await _hypotheses.Add(BenchRepository.H0001Text(), CancellationToken.None);
    }

    [TearDown]
    public void Uncover()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_run_gives_a_ledger_line_a_report_and_a_verdict_together()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        RunResult result = await Runner(server).Run(Options(parity: false), CancellationToken.None);

        Assert.That(result.Verdict!.Kind, Is.EqualTo(VerdictKind.InsufficientData), "four weekly blocks are fewer than 20");
        Assert.That(result.Rows, Has.Count.GreaterThan(50));
        Assert.That(result.Rows.Select(row => row.Instrument).Distinct(), Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }));
        Assert.That(result.Rows, Has.All.Matches<EventRow>(row => row.Moment.Hour == 8));
        LedgerEntry entry = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That(entry.RunId, Is.EqualTo(result.Entry.RunId));
        Assert.That(entry.Status, Is.EqualTo(RunStatus.InsufficientData));
        Assert.That(entry.Version, Is.EqualTo(1));
        Assert.That(entry.EngineVersion, Is.EqualTo(EngineVersion.Number));
        Assert.That(entry.Attempts, Is.EqualTo(1));
        Assert.That(entry.DataHash, Has.Length.EqualTo(64));
        string report = await Report(entry.RunId);
        Assert.That(report, Is.EqualTo(result.Report));
        Assert.That(report, Does.Contain("Данных мало").And.Contain("Исходы событий не читались").And.Not.Contain("Коммит"));
    }

    [Test]
    public async Task A_parity_run_never_computes_the_outcomes_of_events()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        RunResult result = await Runner(server).Run(Options(parity: true), CancellationToken.None);

        Assert.That(result.Verdict, Is.Null);
        Assert.That(result.Rows, Has.All.Matches<EventRow>(row => row.Outcome is null && row.Event is null));
        Assert.That(result.Rows.SelectMany(row => row.Randoms), Has.Some.Matches<RandomTrade>(trade => trade.Outcome.Status == TradeStatus.Ok));
        Assert.That((await _ledger.Entries(CancellationToken.None)).Single().Status, Is.EqualTo(RunStatus.Parity));
        Assert.That(await Report(result.Entry.RunId), Does.Contain("сверочный прогон"));
    }

    [Test]
    public async Task A_run_the_ledger_refuses_reads_no_history_and_leaves_no_line()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await Write(Line("H0001", RunStatus.Rejected, version: 1));

        Assert.That(() => Runner(server).Run(Options(parity: false), CancellationToken.None), Throws.InstanceOf<LedgerException>().With.Message.Contains("already has the verdict"));

        Assert.That(server.Data.CandleRequests, Is.Empty);
        Assert.That(await _ledger.Entries(CancellationToken.None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_new_version_after_a_verdict_runs_as_the_second_attempt()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await Write(Line("H0001", RunStatus.Rejected, version: 1));
        HypothesisAdded second = await _hypotheses.Add(BenchRepository.H0001Text().Replace("0.0005", "0.001", StringComparison.Ordinal), CancellationToken.None);

        RunResult result = await Runner(server).Run(Options(parity: false) with { Version = second.Hypothesis.Version }, CancellationToken.None);

        Assert.That(second.Hypothesis.Version, Is.EqualTo(2));
        Assert.That((result.Entry.Version, result.Entry.Attempts), Is.EqualTo((2, 2)));
        Assert.That(result.Entry.Hash, Is.EqualTo(second.Hypothesis.Hash));
    }

    [Test]
    public async Task A_run_that_fails_on_the_way_leaves_a_failed_line_without_a_report()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Failure = Grpc.Core.StatusCode.Unavailable;

        Assert.That(() => Runner(server).Run(Options(parity: false), CancellationToken.None), Throws.InstanceOf<HistoryUnavailableException>());

        LedgerEntry entry = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That((entry.Status, entry.Version, entry.EngineVersion), Is.EqualTo((RunStatus.Failed, 1, EngineVersion.Number)));
        Assert.That(entry.Reason, Is.Not.Empty);
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM reports"), Is.Zero);
    }

    private HypothesisRunner Runner(MarketDataServer server)
    {
        return new HypothesisRunner(_hypotheses, new GrpcHistorySource(server.Channel), new InstrumentCatalog(server.Http), _ledger, TextWriter.Null);
    }

    private static RunOptions Options(bool parity)
    {
        return new RunOptions("H0001", Version: null, Period.Exploration, parity, Today);
    }

    private Task Write(LedgerEntry entry)
    {
        return _ledger.Decide(_ => new Decision(entry, Report: null), CancellationToken.None);
    }

    private static LedgerEntry Line(string hypothesis, string status, int version)
    {
        return new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis,
            Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Version = version,
            EngineVersion = EngineVersion.Number,
            Period = "exploration",
            Status = status
        };
    }

    private static async Task<string> Report(string runId)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { RunId = runId }) ?? "";
    }
}
