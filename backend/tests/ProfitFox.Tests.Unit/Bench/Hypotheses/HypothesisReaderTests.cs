using System.Text.Json.Nodes;
using ProfitFox.Bench.Hypotheses;
using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Tests.Unit.Bench.Hypotheses;

/// <summary>
///     Spec hypothesis-bench: the hypothesis file, its checks and its hash (task 7.1).
/// </summary>
public sealed class HypothesisReaderTests
{
    public static readonly string H0001Folder = Path.Combine(Repository.Root, "bench", "hypotheses", "H0001-funding-short-4h");

    private static readonly string H0001Text = File.ReadAllText(Path.Combine(H0001Folder, HypothesisReader.FileName));

    [Test]
    public void H0001_is_read_from_the_repository()
    {
        Hypothesis hypothesis = HypothesisReader.Read(H0001Folder);

        Assert.That(hypothesis.Id, Is.EqualTo("H0001"));
        Assert.That(hypothesis.Timeframe, Is.EqualTo(TimeSpan.FromHours(4)));
        Assert.That(hypothesis.Block, Is.EqualTo(TimeBlock.Week));
        Assert.That(hypothesis.Plan, Is.EqualTo(new TradePlan(TradeSide.Short, TimeSpan.FromHours(4))));
        Assert.That(hypothesis.Measure, Is.EqualTo(Measure.Net));
        Assert.That(hypothesis.Costs, Is.EqualTo(TradeCosts.Default));
        Assert.That(hypothesis.Signal.Output, Is.EqualTo("entry"));
        Assert.That(hypothesis.Hash, Has.Length.EqualTo(64));
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
    public void The_folder_is_named_after_the_hypothesis()
    {
        Assert.That(() => HypothesisReader.Parse("/tmp/H0002-other", H0001Text), Throws.InstanceOf<HypothesisException>()
            .With.Property(nameof(HypothesisException.Errors)).EqualTo(new[] { "the folder must be named H0001-<name>" }));
    }

    [Test]
    public void Any_change_of_the_file_changes_the_hash_but_line_endings_do_not()
    {
        string hash = HypothesisReader.Parse(H0001Folder, H0001Text).Hash;
        string changed = HypothesisReader.Parse(H0001Folder, H0001Text.Replace("0.0005", "0.001", StringComparison.Ordinal)).Hash;
        string windows = HypothesisReader.Read(Write(H0001Text.Replace("\n", "\r\n", StringComparison.Ordinal))).Hash;

        Assert.That(changed, Is.Not.EqualTo(hash));
        Assert.That(windows, Is.EqualTo(hash));
    }

    private static HypothesisException Refused(Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(H0001Text)!.AsObject();
        change(file);
        return Assert.Throws<HypothesisException>(() => HypothesisReader.Parse(H0001Folder, file.ToJsonString()))!;
    }

    private static string Write(string text)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"profit-fox-{Guid.NewGuid():N}", "H0001-funding-short-4h");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, HypothesisReader.FileName), text);
        return folder;
    }
}
