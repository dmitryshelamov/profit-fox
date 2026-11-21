using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Hypotheses;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Positions;

/// <summary>
///     Spec strategy-position «Недопустимая заявка» (change add-strategy-graph, task 3.1): the step of a strategy and a
///     side give a trade plan, or the refusal <c>plan</c>; a fraction written in a graph gives the same number as the same
///     fraction written in the plan of a hypothesis.
/// </summary>
public sealed class EntryPlanTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void A_step_with_a_time_and_no_stop_gives_the_plan_of_its_side()
    {
        TradePlan? plan = EntryPlan.Of(Step(FourHours), EntrySide.Short);

        Assert.That(plan, Is.EqualTo(new TradePlan(TradeSide.Short, FourHours)));
    }

    [Test]
    public void A_stop_and_a_target_go_into_the_plan()
    {
        TradePlan? plan = EntryPlan.Of(Step(TimeSpan.FromDays(1), ExitDistance.Of(0.02m), ExitDistance.Of(0.04m)), EntrySide.Long);

        Assert.That(plan, Is.EqualTo(new TradePlan(TradeSide.Long, TimeSpan.FromDays(1), stop: 0.02, target: 0.04)));
    }

    [Test]
    public void A_stop_that_is_not_computed_yet_is_refused()
    {
        Assert.That(EntryPlan.Of(Step(FourHours, stop: ExitDistance.Of(null)), EntrySide.Long), Is.Null);
    }

    [TestCase("1.2")]
    [TestCase("1")]
    [TestCase("0")]
    [TestCase("-0.02")]
    public void A_stop_outside_its_bounds_is_refused(string stop)
    {
        Assert.That(EntryPlan.Of(Step(FourHours, stop: ExitDistance.Of(Number(stop))), EntrySide.Long), Is.Null);
    }

    [Test]
    public void A_target_that_is_missing_or_outside_its_bounds_is_refused()
    {
        Assert.That(EntryPlan.Of(Step(FourHours, target: ExitDistance.Of(null)), EntrySide.Long), Is.Null);
        Assert.That(EntryPlan.Of(Step(FourHours, target: ExitDistance.Of(1.5m)), EntrySide.Long), Is.Null);
    }

    [Test]
    public void A_missing_time_is_refused()
    {
        Assert.That(EntryPlan.Of(Step(after: null), EntrySide.Long), Is.Null);
    }

    [Test]
    public void A_time_shorter_than_a_minute_is_refused()
    {
        Assert.That(EntryPlan.Of(Step(TimeSpan.FromSeconds(30)), EntrySide.Long), Is.Null);
    }

    [Test]
    public void A_time_that_is_not_whole_minutes_is_refused()
    {
        Assert.That(EntryPlan.Of(Step(TimeSpan.FromSeconds(90)), EntrySide.Long), Is.Null);
    }

    [Test]
    public void The_checks_of_a_plan_without_exceptions_are_those_of_its_constructor()
    {
        Assert.That(TradePlan.OrNull(TradeSide.Long, FourHours, stop: 0.02, target: 0.04), Is.EqualTo(new TradePlan(TradeSide.Long, FourHours, 0.02, 0.04)));
        Assert.That(TradePlan.OrNull(TradeSide.Long, FourHours, stop: 1.2, target: null), Is.Null);
        Assert.That(TradePlan.OrNull(TradeSide.Long, FourHours, stop: double.NaN, target: null), Is.Null);
        Assert.That(TradePlan.OrNull(TradeSide.Long, TimeSpan.FromDays(366), stop: null, target: null), Is.Null);
        Assert.That(TradePlan.OrNull((TradeSide)0, FourHours, stop: null, target: null), Is.Null);
    }

    [TestCase("0.02")]
    [TestCase("0.005")]
    [TestCase("0.0125")]
    [TestCase("0.1")]
    [TestCase("0.333")]
    public void A_fraction_in_a_graph_gives_the_plan_of_the_same_fraction_in_the_text_of_a_hypothesis(string fraction)
    {
        TradePlan fromText = HypothesisReader.Parse(HypothesisWith(fraction)).Plan;
        var executor = new StrategyExecutor(StrategyGraph.ReadStrategy(StrategyWith(fraction)).Value!);

        TradePlan? fromGraph = EntryPlan.Of(executor.BeforeCandles(Paths.Start), EntrySide.Short);

        Assert.That(fromGraph, Is.EqualTo(fromText));
        Assert.That(BitConverter.DoubleToInt64Bits(fromGraph!.Stop!.Value), Is.EqualTo(BitConverter.DoubleToInt64Bits(fromText.Stop!.Value)));
        Assert.That(BitConverter.DoubleToInt64Bits(fromGraph.Target!.Value), Is.EqualTo(BitConverter.DoubleToInt64Bits(fromText.Target!.Value)));
        Assert.That(fromText.Stop, Is.EqualTo(double.Parse(fraction, CultureInfo.InvariantCulture)));
    }

    [Test]
    public void A_fraction_of_many_digits_goes_through_its_text_since_the_direct_conversion_differs_in_the_last_bit()
    {
        const string fraction = "0.498964597192726048";
        decimal inGraph = decimal.Parse(fraction, CultureInfo.InvariantCulture);
        TradePlan fromText = HypothesisReader.Parse(HypothesisWith(fraction)).Plan;

        TradePlan? fromGraph = EntryPlan.Of(Step(FourHours, ExitDistance.Of(inGraph), ExitDistance.Of(inGraph)), EntrySide.Short);

        Assert.That((double)inGraph, Is.Not.EqualTo(fromText.Stop), "the reason the plan does not convert the decimal directly");
        Assert.That(fromGraph, Is.EqualTo(fromText));
    }

    // H0001 with the fraction as the stop and the target of its plan.
    private static string HypothesisWith(string fraction)
    {
        JsonObject file = JsonNode.Parse(HypothesisReaderTests.H0001Text)!.AsObject();
        file["plan"]!["stop"] = JsonNode.Parse(fraction);
        file["plan"]!["target"] = JsonNode.Parse(fraction);
        return file.ToJsonString();
    }

    // The strategy of H0001 with the fraction written on a stop-loss and a take-profit node.
    private static string StrategyWith(string fraction)
    {
        return Graphs.ChangedStrategy(graph =>
        {
            graph.AddNode("stop-loss", "stop-loss", "distance", JsonNode.Parse(fraction)!);
            graph.AddNode("take-profit", "take-profit", "distance", JsonNode.Parse(fraction)!);
        });
    }

    private static StrategyStep Step(TimeSpan? after, ExitDistance stop = default, ExitDistance target = default)
    {
        return new StrategyStep(Paths.Start, Entry: null, after, stop, target);
    }

    private static decimal Number(string text)
    {
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }
}
