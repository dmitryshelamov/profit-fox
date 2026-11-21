using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Hypotheses;
using ProfitFox.Presentation.Contracts.Runs;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     The hypotheses of the bench through the API (task 4.2): the list with what can be run, and a text loaded as a version.
/// </summary>
[NonParallelizable]
public sealed class HypothesesTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    [Test]
    public async Task H0001_is_listed_with_its_version_and_every_period_and_kind()
    {
        Assert.That((await Post(BenchRepository.H0001Text())).StatusCode, Is.EqualTo(HttpStatusCode.Created));

        HypothesisResponse h0001 = (await List()).Single();

        Assert.That((h0001.Id, h0001.Version, h0001.Timeframe), Is.EqualTo(("H0001", 1, "4h")));
        Assert.That(
            h0001.Runs.Select(run => (run.Period, run.Kind)),
            Is.EqualTo(new[]
            {
                ("exploration", "verdict"), ("exploration", "parity"), ("holdout", "verdict"), ("holdout", "parity"), ("new-data", "verdict"), ("new-data", "parity")
            }));
    }

    [Test]
    public async Task The_holdout_of_H0001_cannot_be_run_before_exploration_passed_and_says_why()
    {
        await Post(BenchRepository.H0001Text());

        RunAvailabilityResponse[] runs = (await List()).Single().Runs.ToArray();

        RunAvailabilityResponse holdout = runs.Single(run => run is { Period: "holdout", Kind: "verdict" });
        Assert.That(holdout.CanRun, Is.False);
        Assert.That(holdout.Reason, Is.EqualTo("The holdout opens only after H0001 version 1 passed exploration."));
        Assert.That(runs.Single(run => run is { Period: "exploration", Kind: "verdict" }).CanRun, Is.True);
        Assert.That(runs.Single(run => run is { Period: "exploration", Kind: "parity" }).CanRun, Is.True, "a parity run is allowed on exploration");
        Assert.That(runs.Where(run => run is { Kind: "parity", Period: not "exploration" }), Has.All.Matches<RunAvailabilityResponse>(run => !run.CanRun && run.Reason!.Contains("parity run reads only exploration")));
    }

    [Test]
    public async Task A_verdict_blocks_its_version_and_the_next_version_is_free()
    {
        await Post(BenchRepository.H0001Text());
        LedgerEntry verdict = await LedgerLines.Write("H0001", 1, HashOf(BenchRepository.H0001Text()), "exploration", RunStatus.Rejected);

        RunAvailabilityResponse refused = (await List()).Single().Runs.Single(run => run is { Period: "exploration", Kind: "verdict" });
        Assert.That(refused.CanRun, Is.False);
        Assert.That(refused.Reason, Does.Contain("already has the verdict rejected"));
        Assert.That(refused.RunId, Is.EqualTo(verdict.RunId));

        HttpResponseMessage second = await Post(Changed(name => name + "-b"));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        HypothesisResponse latest = (await List()).Single();
        Assert.That(latest.Version, Is.EqualTo(2));
        Assert.That(latest.Runs.Single(run => run is { Period: "exploration", Kind: "verdict" }).CanRun, Is.True, "a new version is a new hypothesis for the rules");
    }

    [Test]
    public async Task A_run_that_waits_blocks_the_same_version_period_and_kind_with_its_id()
    {
        await Post(BenchRepository.H0001Text());
        RunResponse queued = (await (await _api.Http.PostAsJsonAsync("/api/runs", new CreateRunRequest("H0001", Version: null, "exploration", "parity"), Json)).Content.ReadFromJsonAsync<RunResponse>(Json))!;

        RunAvailabilityResponse[] runs = (await List()).Single().Runs.ToArray();

        RunAvailabilityResponse parity = runs.Single(run => run is { Period: "exploration", Kind: "parity" });
        Assert.That((parity.CanRun, parity.RunId), Is.EqualTo((false, queued.Id)));
        Assert.That(parity.Reason, Does.Contain("already waits in the queue"));
        Assert.That(runs.Single(run => run is { Period: "exploration", Kind: "verdict" }).CanRun, Is.True, "the other kind is free");
    }

    [Test]
    public async Task The_same_text_again_answers_200_with_the_version_it_has()
    {
        await Post(BenchRepository.H0001Text());

        HttpResponseMessage again = await Post(BenchRepository.H0001Text().Replace("\n", "\r\n"));

        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        HypothesisAddedResponse added = (await again.Content.ReadFromJsonAsync<HypothesisAddedResponse>(Json))!;
        Assert.That((added.Id, added.Version), Is.EqualTo(("H0001", 1)));
        Assert.That(await List(), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task A_changed_text_is_the_next_version_and_the_list_shows_the_latest()
    {
        await Post(BenchRepository.H0001Text());

        HttpResponseMessage second = await Post(Changed(name => name + "-b"));

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        HypothesisAddedResponse added = (await second.Content.ReadFromJsonAsync<HypothesisAddedResponse>(Json))!;
        Assert.That((added.Id, added.Name, added.Version), Is.EqualTo(("H0001", "funding-short-4h-b", 2)));
        HypothesisResponse listed = (await List()).Single();
        Assert.That((listed.Version, listed.Name), Is.EqualTo((2, "funding-short-4h-b")));
    }

    [Test]
    public async Task A_text_with_errors_is_refused_with_them_and_not_kept()
    {
        JsonObject file = JsonNode.Parse(BenchRepository.H0001Text())!.AsObject();
        file["id"] = "X1";
        file["measure"] = "r";

        HttpResponseMessage refused = await Post(file.ToJsonString());

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        ErrorResponse errors = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(errors.Errors, Is.Not.Empty);
        Assert.That(errors.Errors.Select(error => error.Reason), Has.Some.Contain("id must be H"));
        Assert.That(await List(), Is.Empty);
    }

    [TestCase("")]
    [TestCase("not json")]
    public async Task A_text_that_is_not_a_hypothesis_is_refused_with_422(string text)
    {
        HttpResponseMessage refused = await Post(text);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await refused.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task A_json_body_is_not_a_text()
    {
        HttpResponseMessage refused = await _api.Http.PostAsync("/api/hypotheses", new StringContent(BenchRepository.H0001Text(), Encoding.UTF8, "application/json"));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }

    private async Task<HypothesisResponse[]> List()
    {
        return (await _api.Http.GetFromJsonAsync<HypothesisResponse[]>("/api/hypotheses", Json))!;
    }

    private Task<HttpResponseMessage> Post(string text)
    {
        return _api.Http.PostAsync("/api/hypotheses", new StringContent(text, Encoding.UTF8, "text/plain"));
    }

    private static string Changed(Func<string, string> name)
    {
        JsonObject file = JsonNode.Parse(BenchRepository.H0001Text())!.AsObject();
        file["name"] = name(file["name"]!.GetValue<string>());
        return file.ToJsonString();
    }

    private static string HashOf(string text)
    {
        return ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Parse(ProfitFox.Domain.Services.Hypotheses.HypothesisReader.Normalize(text)).Hash;
    }
}
