using ProfitFox.Cli;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Integration.Presentation;
using ProfitFox.Tests.Integration.Worker;

namespace ProfitFox.Tests.Integration.Cli;

/// <summary>
///     The <c>bench</c> command line tool against the bench API in the test process (spec bench-run-queue «Команды CLI»;
///     change add-strategy-backtest, task 8.4): the commands, what they print and the exit codes. Backtests are computed by a
///     Worker in the test process.
/// </summary>
[NonParallelizable]
public sealed class CliTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_run_with_wait_prints_the_table_of_the_sets_and_the_run_page_and_exits_with_0()
    {
        await SaveFundingShort();
        await using WorkerHost worker = await WorkerHost.Start(_market, "w-1");

        Result result = await Command("run", "funding-short", "--timeframe", "4h", "--param", "threshold=0.0005,0.001", "--wait");

        Assert.That(result.Code, Is.Zero, result.Error);
        string id = QueuedId(result.Output);
        Assert.That(result.Output, Does.Contain($"queued backtest {id} of funding-short v1: 2 sets"));
        Assert.That(result.Output, Does.Contain("set  timeframe  values  trades  mean  sum USDT  wins  difference  t"));
        Assert.That(result.Output, Does.Contain("1  4h  threshold=0.0005  ").And.Contain("2  4h  threshold=0.001  0  -  0  -  -  -"));
        Assert.That(result.Output, Does.Contain($"run page: /runs/{id}"));
        Assert.That(result.Error, Is.Empty);
        Result runs = await Command("runs");
        Assert.That(runs.Code, Is.Zero);
        Assert.That(runs.Output, Does.Contain("active:").And.Contain("finished (last 20):").And.Contain($"{id}  funding-short v1  done  2023-01-01.."));
    }

    [Test]
    public async Task A_run_without_wait_prints_the_id_of_the_queued_backtest_and_exits_with_0_at_once()
    {
        await SaveFundingShort();

        Result result = await Command("run", "funding-short", "--timeframe", "4h", "--from", "2026-01-05", "--until", Backtests.End, "--pairs", "BTCUSDT,ETHUSDT");

        Assert.That(result.Code, Is.Zero, result.Error);
        string id = QueuedId(result.Output);
        Assert.That(result.Output, Does.Not.Contain("run page"));
        Result runs = await Command("runs");
        Assert.That(runs.Output, Does.Contain($"{id}  funding-short v1  idea  1 sets  queued  -  0"));
        BacktestResponseOf read = await Read(id);
        Assert.That((read.Start, read.End, read.Pairs), Is.EqualTo(("2026-01-05", Backtests.End, "BTCUSDT,ETHUSDT")));
    }

    [Test]
    public async Task A_queued_backtest_is_cancelled_and_a_second_cancel_is_refused()
    {
        await SaveFundingShort();

        Result queued = await Command("run", "funding-short");
        string id = QueuedId(queued.Output);
        Result cancelled = await Command("cancel", id);
        Result again = await Command("cancel", id);

        Assert.That(cancelled.Code, Is.Zero, cancelled.Error);
        Assert.That(cancelled.Output, Does.Contain($"cancelling backtest {id}"));
        Assert.That(again.Code, Is.EqualTo(1));
        Assert.That(again.Error, Does.Contain("not cancelled").And.Contain($"There is no backtest {id}"));
    }

    [Test]
    public async Task A_run_on_a_wrong_timeframe_prints_the_reason_of_the_refusal_and_exits_with_1()
    {
        await SaveFundingShort();

        Result result = await Command("run", "funding-short", "--timeframe", "5m");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.StartWith("refused: ").And.Contain("5m"));
        Assert.That(result.Output, Is.Empty);
        Assert.That((await Command("runs")).Output, Does.Not.Contain("queued"));
    }

    [Test]
    public async Task An_unknown_strategy_is_refused_with_1()
    {
        Result result = await Command("run", "price-drop-long");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.StartWith("refused: ").And.Contain("price-drop-long"));
    }

    [Test]
    public async Task A_run_that_failed_prints_the_state_and_the_reason_and_exits_with_1()
    {
        await SaveFundingShort();
        _market.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        await using WorkerHost worker = await WorkerHost.Start(_market, "w-1");

        Result result = await Command("run", "funding-short", "--wait");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Output, Does.Contain("failed: история недоступна").And.Contain($"run page: /runs/{QueuedId(result.Output)}"));
    }

    [TestCase("")]
    [TestCase("frobnicate")]
    [TestCase("run")]
    [TestCase("run funding-short --version")]
    [TestCase("run funding-short --version zero")]
    [TestCase("run funding-short --version 0")]
    [TestCase("run funding-short --nonsense")]
    [TestCase("run funding-short --param threshold")]
    [TestCase("run funding-short --param =0.001")]
    [TestCase("run funding-short --param threshold=")]
    [TestCase("runs now")]
    [TestCase("cancel")]
    [TestCase("parity funding-short")]
    [TestCase("hypotheses add H0001.json")]
    public async Task A_wrong_command_line_prints_the_usage_and_exits_with_2(string line)
    {
        Result result = await Command(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.That(result.Code, Is.EqualTo(2));
        Assert.That(result.Error, Does.Contain("Usage:").And.Contain("bench"));
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task The_help_of_a_command_goes_to_the_output_and_exits_with_0()
    {
        Result result = await Command("run", "--help");

        Assert.That(result.Code, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("bench run <strategy>").And.Contain("--timeframe").And.Contain("--param").And.Contain("--until"));
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public async Task A_bench_that_does_not_answer_exits_with_1()
    {
        using var nobody = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") };

        Result result = await Command(nobody, "runs");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("does not answer"));
    }

    private Task<Result> Command(params string[] args)
    {
        return Command(_api.Http, args);
    }

    private static async Task<Result> Command(HttpClient http, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        int code = await CliApp.Run(args, output, error, http, Poll, timeout.Token);
        return new Result(code, output.ToString(), error.ToString());
    }

    private async Task SaveFundingShort()
    {
        await Backtests.SaveStrategy(_api.Http, "funding-short", Backtests.FundingShort(), "idea");
    }

    private async Task<BacktestResponseOf> Read(string id)
    {
        ProfitFox.Presentation.Contracts.Backtests.BacktestResponse backtest = await Backtests.Read(_api.Http, id);
        return new BacktestResponseOf(backtest.Settings.Start, backtest.Settings.End, string.Join(",", backtest.Settings.Pairs ?? []));
    }

    private static string QueuedId(string output)
    {
        string line = output.Split(Environment.NewLine).First(text => text.StartsWith("queued ", StringComparison.Ordinal));
        return line.Split(' ')[2];
    }

    private sealed record Result(int Code, string Output, string Error);

    private sealed record BacktestResponseOf(string Start, string End, string Pairs);
}
