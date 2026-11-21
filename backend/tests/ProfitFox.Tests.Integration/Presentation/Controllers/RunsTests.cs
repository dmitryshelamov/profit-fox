using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using ProfitFox.Application.Ports.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Runs;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Integration.Worker;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     The runs of the bench through the API (task 4.3): putting a run in the queue, reading the queue and the finished runs,
///     one run with its line and report, and cancelling.
/// </summary>
[NonParallelizable]
public sealed class RunsTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private BenchPresentationServer _api = null!;

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = await BenchPresentationServer.Start(_market);
        await _api.Http.PostAsync("/api/hypotheses", new StringContent(BenchRepository.H0001Text(), Encoding.UTF8, "text/plain"));
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task A_run_is_queued_and_read_back_waiting()
    {
        HttpResponseMessage created = await Enqueue("H0001", "exploration", "verdict");

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        RunResponse run = (await created.Content.ReadFromJsonAsync<RunResponse>(Json))!;
        Assert.That((run.Hypothesis, run.Version, run.Period, run.Kind, run.State), Is.EqualTo(("H0001", 1, "exploration", "verdict", "queued")));
        Assert.That(created.Headers.Location!.ToString(), Does.EndWith($"/api/runs/{run.Id}"));
        RunResponse read = (await _api.Http.GetFromJsonAsync<RunResponse>($"/api/runs/{run.Id}", Json))!;
        Assert.That((read.Id, read.State, read.Result, read.Worker), Is.EqualTo((run.Id, "queued", (RunResultResponse?)null, (string?)null)));
    }

    [Test]
    public async Task The_active_list_is_in_queue_order_with_the_progress_and_the_worker_of_a_run_that_goes()
    {
        RunResponse first = await Queued("H0001", "exploration", "parity");
        RunResponse second = await Queued("H0001", "exploration", "verdict");
        var queue = new RunQueue(BenchDatabase.DataSource);
        RunRecord taken = (await queue.Claim("w-1", TimeSpan.FromMinutes(1), CancellationToken.None))!;
        await queue.Renew(taken.Id, "w-1", TimeSpan.FromMinutes(1), pairsDone: 414, pairsTotal: 828, CancellationToken.None);

        RunResponse[] active = (await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs?active=true", Json))!;

        Assert.That(active.Select(run => run.Id), Is.EqualTo(new[] { first.Id, second.Id }));
        Assert.That((active[0].State, active[0].Worker, active[0].PairsDone, active[0].PairsTotal), Is.EqualTo(("running", "w-1", 414, 828)));
        Assert.That(active[0].StartedAt, Is.Not.Null);
        Assert.That((active[1].State, active[1].Worker, active[1].StartedAt), Is.EqualTo(("queued", (string?)null, (DateTimeOffset?)null)));
        Assert.That((await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs", Json))!.Select(run => run.Id), Is.EqualTo(active.Select(run => run.Id)), "active is the default");
    }

    [Test]
    public async Task The_same_run_again_is_refused_with_409_naming_the_run_that_waits()
    {
        RunResponse waiting = await Queued("H0001", "exploration", "verdict");

        HttpResponseMessage refused = await Enqueue("H0001", "exploration", "verdict");

        RunConflictResponse conflict = await Conflict(refused);
        Assert.That(conflict.RunId, Is.EqualTo(waiting.Id));
        Assert.That(conflict.Reason, Does.Contain("already waits in the queue"));
        Assert.That(await Count("runs"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_version_with_a_verdict_is_refused_with_409_naming_the_line_of_the_ledger()
    {
        LedgerEntry verdict = await LedgerLines.Write("H0001", 1, HashOfH0001(), "exploration", RunStatus.Rejected);

        RunConflictResponse conflict = await Conflict(await Enqueue("H0001", "exploration", "verdict"));

        Assert.That(conflict.RunId, Is.EqualTo(verdict.RunId));
        Assert.That(conflict.Reason, Is.EqualTo($"H0001 version 1 already has the verdict rejected on exploration (run {verdict.RunId})."));
        Assert.That(await Count("runs"), Is.EqualTo(1), "only the finished row of the verdict, nothing queued");
    }

    [Test]
    public async Task The_holdout_before_exploration_passed_is_refused_with_409_and_no_run()
    {
        RunConflictResponse conflict = await Conflict(await Enqueue("H0001", "holdout", "verdict"));

        Assert.That(conflict.Reason, Is.EqualTo("The holdout opens only after H0001 version 1 passed exploration."));
        Assert.That(conflict.RunId, Is.Null);
    }

    [Test]
    public async Task An_unknown_hypothesis_and_an_unknown_version_are_404()
    {
        Assert.That((await Enqueue("H0009", "exploration", "verdict")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        HttpResponseMessage noVersion = await _api.Http.PostAsJsonAsync("/api/runs", new CreateRunRequest("H0001", 7, "exploration", "verdict"), Json);
        Assert.That(noVersion.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_request_with_a_bad_period_kind_or_hypothesis_is_400_with_the_fields()
    {
        HttpResponseMessage refused = await _api.Http.PostAsJsonAsync("/api/runs", new CreateRunRequest(Hypothesis: null, Version: null, "tomorrow", "both"), Json);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors.Select(error => error.Field), Is.EquivalentTo(new[] { "hypothesis", "period", "kind" }));
    }

    [Test]
    public async Task A_waiting_run_is_cancelled_with_204_and_leaves_no_trace()
    {
        RunResponse waiting = await Queued("H0001", "exploration", "verdict");

        HttpResponseMessage cancelled = await _api.Http.PostAsync($"/api/runs/{waiting.Id}/cancel", content: null);

        Assert.That(cancelled.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await _api.Http.GetAsync($"/api/runs/{waiting.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Count("runs") + await Count("ledger"), Is.Zero);
        Assert.That((await Enqueue("H0001", "exploration", "verdict")).StatusCode, Is.EqualTo(HttpStatusCode.Created), "it can be queued again");
    }

    [Test]
    public async Task A_run_that_goes_is_asked_to_stop_with_204()
    {
        RunResponse queued = await Queued("H0001", "exploration", "verdict");
        await new RunQueue(BenchDatabase.DataSource).Claim("w-1", TimeSpan.FromMinutes(1), CancellationToken.None);

        HttpResponseMessage cancelled = await _api.Http.PostAsync($"/api/runs/{queued.Id}/cancel", content: null);

        Assert.That(cancelled.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        RunResponse read = (await _api.Http.GetFromJsonAsync<RunResponse>($"/api/runs/{queued.Id}", Json))!;
        Assert.That((read.State, read.CancelRequested), Is.EqualTo(("running", true)));
    }

    [Test]
    public async Task An_unknown_run_is_404_to_read_and_to_cancel()
    {
        Assert.That((await _api.Http.GetAsync("/api/runs/nosuchrun")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.PostAsync("/api/runs/nosuchrun/cancel", content: null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_finished_run_has_its_line_and_report_and_cannot_be_cancelled()
    {
        RunResponse queued = await Queued("H0001", "exploration", "verdict");
        await using WorkerHost worker = await WorkerHost.Start(_market, "w-1");
        await Poll.Until("the run ends", async () => (await _api.Http.GetFromJsonAsync<RunResponse>($"/api/runs/{queued.Id}", Json))!.State == "finished");

        RunResponse finished = (await _api.Http.GetFromJsonAsync<RunResponse>($"/api/runs/{queued.Id}", Json))!;

        RunResultResponse result = finished.Result!;
        Assert.That(result.Status, Is.EqualTo(RunStatus.InsufficientData));
        Assert.That((result.Version, result.EngineVersion, result.Attempts, result.Blocks), Is.EqualTo((1, EngineVersion.Number, 1, 4)));
        Assert.That(result.DataHash, Has.Length.EqualTo(64));
        Assert.That(result.Power, Is.Not.Null);
        Assert.That(result.Report, Does.StartWith("# H0001 funding-short-4h: exploration").And.Contain($"| Версия движка | {EngineVersion.Number} |"));
        Assert.That(finished.FinishedAt, Is.Not.Null);
        Assert.That((await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs?active=true", Json))!, Is.Empty);

        RunResponse[] list = (await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs?active=false&limit=5", Json))!;
        Assert.That(list.Single().Id, Is.EqualTo(queued.Id));
        Assert.That((list[0].Kind, list[0].Result!.Status, list[0].Result!.Report), Is.EqualTo(("verdict", RunStatus.InsufficientData, (string?)null)), "the list leaves the report out");

        RunConflictResponse conflict = await Conflict(await _api.Http.PostAsync($"/api/runs/{queued.Id}/cancel", content: null));
        Assert.That(conflict.RunId, Is.EqualTo(queued.Id));
    }

    [Test]
    public async Task Finished_runs_are_listed_newest_first_and_the_limit_cuts_the_list()
    {
        LedgerEntry first = await LedgerLines.Write("H0001", 1, HashOfH0001(), "exploration", RunStatus.Parity);
        LedgerEntry second = await LedgerLines.Write("H0001", 1, HashOfH0001(), "holdout", RunStatus.Parity);
        LedgerEntry third = await LedgerLines.Write("H0001", 1, HashOfH0001(), "new-data", RunStatus.Failed);

        RunResponse[] all = (await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs?active=false", Json))!;
        RunResponse[] two = (await _api.Http.GetFromJsonAsync<RunResponse[]>("/api/runs?active=false&limit=2", Json))!;

        Assert.That(all.Select(run => run.Id), Is.EqualTo(new[] { third.RunId, second.RunId, first.RunId }));
        Assert.That(two.Select(run => run.Id), Is.EqualTo(new[] { third.RunId, second.RunId }));
        Assert.That(all.Select(run => run.Kind), Is.EqualTo(new[] { "verdict", "parity", "parity" }), "the kind is that of the row of the run");
    }

    [Test]
    public async Task A_preview_leaves_the_queue_and_the_ledger_as_they_were()
    {
        RunResponse waiting = await Queued("H0001", "exploration", "verdict");
        await LedgerLines.Write("H0001", 1, HashOfH0001(), "exploration", RunStatus.Parity);
        (int runs, int ledger, int reports) before = (await Count("runs"), await Count("ledger"), await Count("reports"));

        JsonObject signal = JsonNode.Parse(BenchRepository.H0001Text())!["signal"]!.DeepClone().AsObject();
        HttpResponseMessage preview = await _api.Http.PostAsJsonAsync("/api/preview", new JsonObject { ["instrument"] = "BTCUSDT", ["timeframe"] = "4h", ["signal"] = signal }, Json);

        Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK), await preview.Content.ReadAsStringAsync());
        Assert.That((await Count("runs"), await Count("ledger"), await Count("reports")), Is.EqualTo(before));
        Assert.That((await _api.Http.GetFromJsonAsync<RunResponse>($"/api/runs/{waiting.Id}", Json))!.State, Is.EqualTo("queued"));
    }

    private Task<HttpResponseMessage> Enqueue(string hypothesis, string period, string kind)
    {
        return _api.Http.PostAsJsonAsync("/api/runs", new CreateRunRequest(hypothesis, Version: null, period, kind), Json);
    }

    private async Task<RunResponse> Queued(string hypothesis, string period, string kind)
    {
        HttpResponseMessage created = await Enqueue(hypothesis, period, kind);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<RunResponse>(Json))!;
    }

    private static async Task<RunConflictResponse> Conflict(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RunConflictResponse>(Json))!;
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private static string HashOfH0001()
    {
        return ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Parse(ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Normalize(BenchRepository.H0001Text())).Hash;
    }
}
