using System.Net;
using System.Text.Json.Nodes;

using ProfitFox.Cli;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Tests.Integration.Database;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Integration.Presentation;
using ProfitFox.Tests.Integration.Workers;

namespace ProfitFox.Tests.Integration.Cli;

/// <summary>
///     The <c>bench</c> command line tool against the bench API in the test process (task 5.1, spec bench-run-queue «Команды
///     CLI»): the commands, what they print and the exit codes. Runs are counted by a Worker in the test process.
/// </summary>
[NonParallelizable]
public sealed class CliTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private MarketDataServer _market = null!;
    private BenchPresentationServer _api = null!;
    private string _folder = null!;

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = await BenchPresentationServer.Start(_market);
        _folder = Directory.CreateTempSubdirectory("bench-cli").FullName;
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        FakeMarketData.CoveredUntil = null;
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task A_run_with_wait_prints_the_verdict_with_its_reason_and_the_run_page_and_exits_with_0()
    {
        await AddH0001();
        await using WorkerHost worker = await WorkerHost.Start(_market, "w-1");

        Result result = await Command("run", "H0001", "--wait");

        Assert.That(result.Code, Is.Zero, result.Error);
        string id = QueuedId(result.Output);
        Assert.That(result.Output, Does.Contain("insufficient_data:").And.Contain("power ").And.Contain($"run page: /runs/{id}"));
        Assert.That(result.Error, Is.Empty);
        Result runs = await Command("runs");
        Assert.That(runs.Code, Is.Zero);
        Assert.That(runs.Output, Does.Contain("active:").And.Contain("finished (last 20):").And.Contain($"{id}  H0001 v1  exploration  insufficient_data"));
    }

    [Test]
    public async Task A_run_without_wait_prints_the_id_of_the_queued_run_and_exits_with_0_at_once()
    {
        await AddH0001();

        Result result = await Command("run", "H0001", "--period", "exploration");

        Assert.That(result.Code, Is.Zero, result.Error);
        string id = QueuedId(result.Output);
        Assert.That(result.Output, Does.Not.Contain("run page"));
        Result runs = await Command("runs");
        Assert.That(runs.Output, Does.Contain($"{id}  H0001 v1  exploration  verdict  queued  -  0"));
    }

    [Test]
    public async Task A_parity_run_is_queued_on_exploration_and_a_queued_run_is_cancelled()
    {
        await AddH0001();

        Result queued = await Command("parity", "H0001");
        string id = QueuedId(queued.Output);
        Result cancelled = await Command("cancel", id);
        Result again = await Command("cancel", id);

        Assert.That(queued.Output, Does.Contain("parity run").And.Contain("on exploration"));
        Assert.That(cancelled.Code, Is.Zero, cancelled.Error);
        Assert.That(again.Code, Is.EqualTo(1));
        Assert.That(again.Error, Does.Contain("not cancelled").And.Contain($"There is no run {id}"));
    }

    [Test]
    public async Task A_run_the_ledger_refuses_prints_the_reason_and_exits_with_1()
    {
        await AddH0001();
        await LedgerLines.Write("H0001", 1, HypothesisHash(BenchRepository.H0001Text()), "exploration", RunStatus.Rejected);

        Result result = await Command("run", "H0001");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.StartWith("refused: ").And.Contain("H0001"));
        Assert.That(result.Output, Is.Empty);
        Assert.That((await Command("runs")).Output, Does.Not.Contain("queued"));
    }

    [Test]
    public async Task An_unknown_hypothesis_is_refused_with_1()
    {
        Result result = await Command("run", "H9999");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.StartWith("refused: ").And.Contain("H9999"));
    }

    [Test]
    public async Task A_run_that_failed_prints_the_status_and_the_reason_and_exits_with_1()
    {
        await AddH0001();
        _market.Data.Failure = Grpc.Core.StatusCode.Unavailable;
        await using WorkerHost worker = await WorkerHost.Start(_market, "w-1");

        Result result = await Command("run", "H0001", "--wait");

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Output, Does.Contain("failed: ").And.Contain("market-data").And.Contain($"run page: /runs/{QueuedId(result.Output)}"));
    }

    [Test]
    public async Task A_hypothesis_file_is_loaded_as_a_version_and_the_same_file_again_finds_it()
    {
        string first = Path.Combine(_folder, "first.json");
        string second = Path.Combine(_folder, "second.json");
        await File.WriteAllTextAsync(first, BenchRepository.H0001Text());
        JsonObject changed = JsonNode.Parse(BenchRepository.H0001Text())!.AsObject();
        changed["name"] = changed["name"]!.GetValue<string>() + "-b";
        await File.WriteAllTextAsync(second, changed.ToJsonString());

        Result created = await Command("hypotheses", "add", first);
        Result found = await Command("hypotheses", "add", first);
        Result next = await Command("hypotheses", "add", second);

        Assert.That(created.Output.Trim(), Is.EqualTo("H0001 version 1 created"));
        Assert.That(found.Output.Trim(), Is.EqualTo("H0001 already exists as version 1"));
        Assert.That(next.Output.Trim(), Is.EqualTo("H0001 version 2 created"));
        Assert.That(new[] { created.Code, found.Code, next.Code }, Is.All.Zero);
    }

    [Test]
    public async Task A_hypothesis_file_with_errors_prints_them_and_exits_with_1()
    {
        string file = Path.Combine(_folder, "broken.json");
        await File.WriteAllTextAsync(file, "{ \"id\": \"H0002\" }");

        Result result = await Command("hypotheses", "add", file);

        Assert.That(result.Code, Is.EqualTo(1));
        Assert.That(result.Error, Does.StartWith("refused: ").And.Length.GreaterThan("refused: \n".Length));
        Assert.That((await Command("hypotheses", "add", Path.Combine(_folder, "missing.json"))).Code, Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("frobnicate")]
    [TestCase("run")]
    [TestCase("run H0001 --period yesterday")]
    [TestCase("run H0001 --version")]
    [TestCase("run H0001 --version zero")]
    [TestCase("run H0001 --version 0")]
    [TestCase("run H0001 --nonsense")]
    [TestCase("parity H0001 --period holdout")]
    [TestCase("runs now")]
    [TestCase("cancel")]
    [TestCase("hypotheses")]
    [TestCase("hypotheses add")]
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
        Assert.That(result.Output, Does.Contain("bench run <hypothesis>").And.Contain("--period"));
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

    private async Task AddH0001()
    {
        HttpResponseMessage added = await _api.Http.PostAsync("/api/hypotheses", new StringContent(BenchRepository.H0001Text(), System.Text.Encoding.UTF8, "text/plain"));
        Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    private static string QueuedId(string output)
    {
        string line = output.Split(Environment.NewLine).First(text => text.StartsWith("queued ", StringComparison.Ordinal));
        return line.Split(' ')[3];
    }

    private static string HypothesisHash(string text)
    {
        return ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Parse(ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Normalize(text)).Hash;
    }

    private sealed record Result(int Code, string Output, string Error);
}
