using ProfitFox.Bench.Cli;
using ProfitFox.Bench.Hypotheses;
using ProfitFox.Bench.Ledger;
using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.MarketData.History;
using ProfitFox.Bench.Runs;
using ProfitFox.Bench.Verdicts;
using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench end to end (task 7.6): H0001 on four weeks of history of a fake market-data service gives a
///     line in the ledger, a report and a verdict; a parity run writes its dump without event outcomes.
/// </summary>
[NonParallelizable]
public sealed class HypothesisRunTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private string _bench = "";
    private string _hypothesis = "";

    [SetUp]
    public void CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        _bench = Path.Combine(Path.GetTempPath(), $"profit-fox-bench-{Guid.NewGuid():N}", "bench");
        _hypothesis = Path.Combine(_bench, "hypotheses", "H0001-funding-short-4h");
        Directory.CreateDirectory(_hypothesis);
        File.Copy(Path.Combine(Root(), "bench", "hypotheses", "H0001-funding-short-4h", HypothesisReader.FileName), Path.Combine(_hypothesis, HypothesisReader.FileName));
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
        Directory.Delete(Path.GetDirectoryName(_bench)!, recursive: true);
    }

    [Test]
    public async Task A_run_gives_a_ledger_line_a_report_and_a_verdict()
    {
        await using MarketDataServer server = await MarketDataServer.Start();

        RunResult result = await Runner(server, new FakeRepository()).Run(Options(parity: false, KeptDump("run")), CancellationToken.None);

        Assert.That(result.Verdict!.Kind, Is.EqualTo(VerdictKind.InsufficientData), "four weekly blocks are fewer than 20");
        Assert.That(result.Rows, Has.Count.GreaterThan(50));
        Assert.That(result.Rows.Select(row => row.Instrument).Distinct(), Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }));
        Assert.That(result.Rows, Has.All.Matches<EventRow>(row => row.Moment.Hour == 8));
        LedgerEntry entry = new RunLedger(Path.Combine(_bench, "ledger.jsonl")).Entries().Single();
        Assert.That(entry.Status, Is.EqualTo(RunStatus.InsufficientData));
        Assert.That(entry.DataHash, Has.Length.EqualTo(64));
        Assert.That(File.ReadAllText(result.ReportPath), Does.Contain("Данных мало").And.Contain("Исходы событий не читались"));
    }

    [Test]
    public async Task A_parity_run_never_computes_the_outcomes_of_events()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        string dump = KeptDump("parity") ?? Path.Combine(_bench, "..", "dump");

        RunResult result = await Runner(server, new FakeRepository()).Run(Options(parity: true, dump), CancellationToken.None);

        Assert.That(result.Verdict, Is.Null);
        Assert.That(result.Rows, Has.All.Matches<EventRow>(row => row.Outcome is null && row.Event is null));
        Assert.That(result.Rows.SelectMany(row => row.Randoms), Has.Some.Matches<RandomTrade>(trade => trade.Outcome.Status == TradeStatus.Ok));
        Assert.That(File.Exists(Path.Combine(dump, "randoms.csv.gz")), Is.True);
        Assert.That(File.Exists(Path.Combine(dump, "event_outcomes.csv.gz")), Is.False);
        Assert.That(new RunLedger(Path.Combine(_bench, "ledger.jsonl")).Entries().Single().Status, Is.EqualTo(RunStatus.Parity));
    }

    [Test]
    public async Task Uncommitted_changes_stop_the_run_before_any_history_is_read()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var repository = new FakeRepository();
        repository.Changes.Add("backend/src/engine/ProfitFox.Engine.Strategy/Nodes/Data/FundingRateNode.cs");

        Assert.That(() => Runner(server, repository).Run(Options(parity: false, dump: null), CancellationToken.None), Throws.InstanceOf<UncommittedChangesException>());
        Assert.That(server.Data.CandleRequests, Is.Empty);
        Assert.That(File.Exists(Path.Combine(_bench, "ledger.jsonl")), Is.False);
    }

    [Test]
    public async Task A_run_that_fails_on_the_way_leaves_a_failed_line()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Failure = Grpc.Core.StatusCode.Unavailable;

        Assert.That(() => Runner(server, new FakeRepository()).Run(Options(parity: false, dump: null), CancellationToken.None), Throws.InstanceOf<HistoryUnavailableException>());
        Assert.That(new RunLedger(Path.Combine(_bench, "ledger.jsonl")).Entries().Single().Status, Is.EqualTo(RunStatus.Failed));
    }

    [Test]
    public void The_command_line_is_parsed()
    {
        Assert.That(CommandLine.Parse(["run", "bench/hypotheses/H0001-funding-short-4h"], out _),
            Is.EqualTo(new BenchCommand("bench/hypotheses/H0001-funding-short-4h", Period.Exploration, false, null)));
        Assert.That(CommandLine.Parse(["run", "h", "--period", "holdout", "--parity-dump", "/tmp/d"], out _), Is.EqualTo(new BenchCommand("h", Period.Holdout, false, "/tmp/d")));
        Assert.That(CommandLine.Parse(["parity", "h", "--out", "/tmp/p"], out _), Is.EqualTo(new BenchCommand("h", Period.Exploration, true, "/tmp/p")));
        Assert.That(CommandLine.Parse(["parity", "h"], out string? error), Is.Null);
        Assert.That(error, Does.Contain("--out"));
        Assert.That(CommandLine.Parse(["run", "h", "--period", "later"], out _), Is.Null);
    }

    // PROFIT_FOX_KEEP_DUMP=<folder> keeps the dumps of these runs there, to look at them.
    private static string? KeptDump(string name)
    {
        return Environment.GetEnvironmentVariable("PROFIT_FOX_KEEP_DUMP") is { Length: > 0 } folder ? Path.Combine(folder, name) : null;
    }

    private HypothesisRunner Runner(MarketDataServer server, IRepository repository)
    {
        var catalog = new InstrumentCatalog(server.Http);
        return new HypothesisRunner(new GrpcHistorySource(server.Channel), catalog.LinearUsdtPerpetuals, repository, _bench, TextWriter.Null);
    }

    private RunOptions Options(bool parity, string? dump)
    {
        return new RunOptions(_hypothesis, Period.Exploration, parity, dump, Today);
    }

    private static string Root()
    {
        DirectoryInfo? folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}
