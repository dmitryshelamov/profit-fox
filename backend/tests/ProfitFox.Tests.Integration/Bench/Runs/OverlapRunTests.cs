using Dapper;
using Npgsql;
using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Infrastructure.Hypotheses;
using ProfitFox.Infrastructure.Ledger;
using ProfitFox.Infrastructure.MarketData;
using ProfitFox.Domain.Trades;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench «Событие при открытой позиции» and «Момент конца цикла до вердикта» (change
///     add-strategy-graph, task 4.3), end to end. The hypothesis <c>Fixtures/H0002.json</c> is H0001 with a short for two
///     days. On the history of the fake market-data service every pair has an event each day at 08:00 for four weeks, so
///     every second event comes while the position is open. The history ends on 2026-02-02, so the trades of the last two
///     events of a pair are left out for coverage and hold nothing.
/// </summary>
[NonParallelizable]
public sealed class OverlapRunTests
{
    private const int Pairs = 3;

    // Of the 28 daily events of a pair: 13 open a cycle, 13 come while one is open, the last two are left out for coverage.
    private const int OpenedPerPair = 13;
    private const int SkippedPerPair = 13;
    private const int LeftOutPerPair = 2;

    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    private readonly HypothesisCatalog _hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));
    private readonly RunLedger _ledger = new RunLedger(BenchDatabase.DataSource);

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        await _hypotheses.Add(File.ReadAllText(Path.Combine(Fixtures, "H0002.json")), CancellationToken.None);
    }

    [TearDown]
    public void Uncover()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_parity_run_leaves_the_skipped_events_out_and_computes_no_outcome_of_an_event()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        RunResult result = await Runner(server).Run(Options(parity: true), CancellationToken.None);

        LedgerEntry entry = (await _ledger.Entries(CancellationToken.None)).Single();
        Assert.That(entry.Status, Is.EqualTo(RunStatus.Parity));
        Assert.That(result.Skipped, Is.EqualTo(Pairs * SkippedPerPair));
        Assert.That(result.Rows, Has.Count.EqualTo(Pairs * (OpenedPerPair + LeftOutPerPair)), "a skipped event has no row");
        Assert.That(entry.Events, Is.EqualTo(Pairs * OpenedPerPair), "the events of the parity line are those that opened a cycle");
        Assert.That(result.Rows, Has.All.Matches<EventRow>(row => row.Outcome is null && row.Event is null), "no result of a cycle of an event");
        Assert.That(result.Rows.SelectMany(row => row.Randoms), Has.Some.Matches<RandomTrade>(trade => trade.Outcome.Status == TradeStatus.Ok));
        foreach (IGrouping<string, EventRow> pair in result.Rows.GroupBy(row => row.Instrument))
        {
            DateTimeOffset[] opened = [.. pair.Where(row => row.Status == TradeStatus.Ok).Select(row => row.Moment)];
            Assert.That(opened, Has.Length.EqualTo(OpenedPerPair), pair.Key);
            Assert.That(opened.Zip(opened.Skip(1), (earlier, later) => later - earlier), Has.All.EqualTo(TimeSpan.FromDays(2)), "the event at the exit of a cycle opens the next");
            Assert.That(pair.Where(row => row.Status != TradeStatus.Ok).Select(row => row.Status), Is.EqualTo(Enumerable.Repeat(TradeStatus.Coverage, LeftOutPerPair)));
        }

        string report = await Report(result.Entry.RunId);
        Assert.That(report, Does.Contain("сверочный прогон").And.Contain($"| Событий пропущено при открытой позиции | {Pairs * SkippedPerPair} |"));
    }

    [Test]
    public async Task A_run_with_a_verdict_skips_the_same_events_as_the_parity_run()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        RunResult parity = await Runner(server).Run(Options(parity: true), CancellationToken.None);

        RunResult run = await Runner(server).Run(Options(parity: false), CancellationToken.None);

        Assert.That(run.Skipped, Is.EqualTo(parity.Skipped));
        Assert.That(run.Rows.Select(row => (row.Instrument, row.Moment, row.Status)), Is.EqualTo(parity.Rows.Select(row => (row.Instrument, row.Moment, row.Status))));
        Assert.That(
            run.Rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray()),
            Is.EqualTo(parity.Rows.Select(row => row.Randoms.Select(random => random.Entry).ToArray())));
        Assert.That(run.Rows.Where(row => row.Status == TradeStatus.Ok), Has.All.Matches<EventRow>(row => row.Outcome!.ExitTime == row.Moment.AddDays(2)));
    }

    private HypothesisRunner Runner(MarketDataServer server)
    {
        return new HypothesisRunner(_hypotheses, new GrpcHistorySource(server.Channel), new InstrumentCatalog(server.Http), _ledger, TextWriter.Null);
    }

    private static RunOptions Options(bool parity)
    {
        return new RunOptions("H0002", Version: null, Period.Exploration, parity, Today);
    }

    private static async Task<string> Report(string runId)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { RunId = runId }) ?? "";
    }
}
