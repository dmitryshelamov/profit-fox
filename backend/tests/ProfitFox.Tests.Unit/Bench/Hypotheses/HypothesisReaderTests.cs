using System.Text.Json.Nodes;
using ProfitFox.Bench.Domain.Hypotheses;
using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Tests.Unit.Bench.Hypotheses;

/// <summary>
///     Spec hypothesis-bench: the hypothesis text, its checks and its hash (task 7.1).
/// </summary>
public sealed class HypothesisReaderTests
{
    public static readonly string H0001Folder = Path.Combine(Repository.Root, "bench", "hypotheses", "H0001-funding-short-4h");

    public static readonly string H0001Text = File.ReadAllText(Path.Combine(H0001Folder, "hypothesis.json"));

    [Test]
    public void H0001_is_read_from_the_repository()
    {
        Hypothesis hypothesis = HypothesisReader.Parse(H0001Text);

        Assert.That(hypothesis.Id, Is.EqualTo("H0001"));
        Assert.That(hypothesis.Timeframe, Is.EqualTo(TimeSpan.FromHours(4)));
        Assert.That(hypothesis.Block, Is.EqualTo(TimeBlock.Week));
        Assert.That(hypothesis.Plan, Is.EqualTo(new TradePlan(TradeSide.Short, TimeSpan.FromHours(4))));
        Assert.That(hypothesis.Measure, Is.EqualTo(Measure.Net));
        Assert.That(hypothesis.Costs, Is.EqualTo(TradeCosts.Default));
        Assert.That(hypothesis.Signal.Output, Is.EqualTo("entry"));
        Assert.That(hypothesis.Hash, Has.Length.EqualTo(64));
        Assert.That(hypothesis.Version, Is.EqualTo(1));
    }

    [Test]
    public void The_hash_of_H0001_is_the_one_of_the_ledger()
    {
        Assert.That(HypothesisReader.Parse(H0001Text).Hash, Does.StartWith("5591d11a"));
    }

    [Test]
    public void The_version_is_the_one_given_to_the_reader()
    {
        Assert.That(HypothesisReader.Parse(H0001Text, version: 3).Version, Is.EqualTo(3));
    }

    [Test]
    public void An_id_that_is_not_H_and_four_digits_is_refused()
    {
        HypothesisException error = Refused(file => file["id"] = "X1");

        Assert.That(error.Errors, Is.EqualTo(new[] { "id must be H followed by four digits" }));
    }

    [Test]
    public void Measure_R_without_a_stop_is_refused()
    {
        HypothesisException error = Refused(file => file["measure"] = "r");

        Assert.That(error.Errors, Is.EqualTo(new[] { "measure r needs a stop in the plan" }));
    }

    [Test]
    public void A_block_shorter_than_the_horizon_is_refused()
    {
        HypothesisException error = Refused(file =>
        {
            file["block"] = "day";
            file["plan"]!["horizon"] = "2d";
        });

        Assert.That(error.Errors, Is.EqualTo(new[] { "the block must not be shorter than the horizon of the plan" }));
    }

    [Test]
    public void Every_problem_is_reported_at_once()
    {
        HypothesisException error = Refused(file =>
        {
            file["randomsPerEvent"] = 1;
            file["timeframe"] = "2h";
            file["typo"] = true;
            file["plan"]!["stop"] = 1.5;
            file["signal"]!["formatVersion"] = 2;
        });

        Assert.That(error.Errors, Has.Count.EqualTo(5));
        Assert.That(error.Errors, Has.Some.StartsWith("unknown field typo"));
        Assert.That(error.Errors, Has.Some.StartsWith("plan.stop"));
        Assert.That(error.Errors, Has.Some.StartsWith("signal: UnsupportedFormatVersion"));
    }

    [Test]
    public void Any_change_of_the_file_changes_the_hash_but_line_endings_do_not()
    {
        string hash = HypothesisReader.Parse(H0001Text).Hash;
        string changed = HypothesisReader.Parse(H0001Text.Replace("0.0005", "0.001", StringComparison.Ordinal)).Hash;
        string windows = HypothesisReader.Parse(HypothesisReader.Normalize(H0001Text.Replace("\n", "\r\n", StringComparison.Ordinal))).Hash;

        Assert.That(changed, Is.Not.EqualTo(hash));
        Assert.That(windows, Is.EqualTo(hash));
    }

    private static HypothesisException Refused(Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(H0001Text)!.AsObject();
        change(file);
        return Assert.Throws<HypothesisException>(() => HypothesisReader.Parse(file.ToJsonString()))!;
    }
}
