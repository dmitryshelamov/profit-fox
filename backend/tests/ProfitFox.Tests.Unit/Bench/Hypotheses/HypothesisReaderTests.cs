using System.Text.Json.Nodes;
using ProfitFox.Domain.Hypotheses;
using ProfitFox.Engine.Simulation.Positions;
using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Tests.Unit.Engine.Strategy.Graph;

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

    [Test]
    public void H0001_carries_the_strategy_of_its_signal_and_its_plan()
    {
        Hypothesis hypothesis = HypothesisReader.Parse(H0001Text);

        Assert.That(
            StrategyGraph.Write(hypothesis.Strategy.Graph),
            Is.EqualTo(StrategyGraph.Write(StrategyGraph.Read(Graphs.H0001Strategy).Value!)),
            "the nodes of the signal of H0001, a short entry and a close after 4h, without a stop and a target");
        Assert.That(hypothesis.Strategy.Entries, Is.EqualTo(new[] { new StrategyEntry("enter", EntrySide.Short) }));
        Assert.That(hypothesis.Hash, Does.StartWith("5591d11a"), "the hash is the one of the ledger");
        Assert.That(hypothesis.Version, Is.EqualTo(1));
        Assert.That(hypothesis.Signal.Output, Is.EqualTo("entry"), "the signal is still read as written");
    }

    [Test]
    public void A_stop_and_a_target_go_into_the_strategy_as_the_decimals_of_the_text()
    {
        Hypothesis hypothesis = HypothesisReader.Parse(Changed(file =>
        {
            file["plan"]!["side"] = "long";
            file["plan"]!["horizon"] = "1d";
            file["plan"]!["stop"] = JsonNode.Parse("0.02");
            file["plan"]!["target"] = JsonNode.Parse("0.333");
        }));

        NodeDefinition[] nodes = [.. hypothesis.Strategy.Graph.Nodes];
        Assert.That(nodes.Single(node => node.Kind == "stop-loss").Inputs["distance"], Is.EqualTo(GraphValue.FromNumber(0.02m)));
        Assert.That(nodes.Single(node => node.Kind == "take-profit").Inputs["distance"], Is.EqualTo(GraphValue.FromNumber(0.333m)));
        Assert.That(nodes.Single(node => node.Kind == "close-after").Inputs["after"], Is.EqualTo(GraphValue.FromDuration(TimeSpan.FromDays(1))));
        Assert.That(hypothesis.Strategy.Entries.Single().Side, Is.EqualTo(EntrySide.Long));
        Assert.That(hypothesis.Plan, Is.EqualTo(new TradePlan(TradeSide.Long, TimeSpan.FromDays(1), stop: 0.02, target: 0.333)));
    }

    [Test]
    public void The_strategy_of_a_hypothesis_gives_the_plan_of_the_hypothesis()
    {
        Hypothesis hypothesis = HypothesisReader.Parse(Changed(file =>
        {
            file["plan"]!["stop"] = JsonNode.Parse("0.0125");
            file["plan"]!["target"] = JsonNode.Parse("0.1");
        }));

        StrategyStep written = new StrategyExecutor(hypothesis.Strategy).BeforeCandles(DateTimeOffset.UnixEpoch);

        Assert.That(EntryPlan.Of(written, hypothesis.Strategy.Entries.Single().Side), Is.EqualTo(hypothesis.Plan));
    }

    [Test]
    public void A_stop_the_strategy_cannot_carry_exactly_is_refused()
    {
        // The plan reads 1e-30 as a double above 0; a graph counts in decimal, which has no such small number.
        HypothesisException error = Refused(file => file["plan"]!["stop"] = JsonNode.Parse("1e-30"));

        Assert.That(error.Errors, Is.EqualTo(new[] { "plan: stop and target must be decimal numbers a graph can hold, of at most 28 digits" }));
    }

    private static string Changed(Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(H0001Text)!.AsObject();
        change(file);
        return file.ToJsonString();
    }

    private static HypothesisException Refused(Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(H0001Text)!.AsObject();
        change(file);
        return Assert.Throws<HypothesisException>(() => HypothesisReader.Parse(file.ToJsonString()))!;
    }
}
