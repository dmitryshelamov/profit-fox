using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Nodes;

/// <summary>
///     Each node on a reference dataset: the expected value on every close, and, for the nodes that read the past or the funding
///     payments, a check that cutting the data at a moment changes no value before it.
/// </summary>
[TestFixture]
public class NodeTests
{
    private static readonly string Rate = Graphs.NumberOutput("""{ "id": "rate", "kind": "funding-rate" }""", wires: "", "rate.value");

    private static readonly string PreviousRate = Graphs.NumberOutput(
        """{ "id": "rate", "kind": "funding-rate" }, { "id": "prev", "kind": "previous" }""",
        """{ "from": "rate.value", "to": "prev.value" }""",
        "prev.result");

    private static readonly ClosedCandle[] Closes =
    [
        Feed.Candle("BTCUSDT", "2026-01-01T04:00"),
        Feed.Candle("BTCUSDT", "2026-01-01T08:00"),
        Feed.Candle("BTCUSDT", "2026-01-01T12:00"),
        Feed.Candle("BTCUSDT", "2026-01-01T16:00")
    ];

    // Payments 7, 9, 4 just before the first three closes, so the rate on the closes is 7, 9, 4, 4.
    private static readonly FundingPayment[] SevenNineFour =
    [
        Feed.Payment("BTCUSDT", "2026-01-01T03:00", rate: 7m),
        Feed.Payment("BTCUSDT", "2026-01-01T07:00", rate: 9m),
        Feed.Payment("BTCUSDT", "2026-01-01T11:00", rate: 4m)
    ];

    [Test]
    public void Funding_rate_includes_a_payment_at_the_close()
    {
        List<CloseResult> results = Feed.Run(
            Graphs.Valid(Rate),
            [Feed.Candle("BTCUSDT", "2026-01-01T08:00")],
            [Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m), Feed.Payment("BTCUSDT", "2026-01-01T08:00", rate: 0.0006m)]);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { 0.0006m }));
    }

    [Test]
    public void Funding_rate_between_payments_is_the_earlier_one()
    {
        List<CloseResult> results = Feed.Run(
            Graphs.Valid(Rate),
            [Feed.Candle("BTCUSDT", "2026-01-01T04:00")],
            [Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m), Feed.Payment("BTCUSDT", "2026-01-01T08:00", rate: 0.0006m)]);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { 0.0001m }));
    }

    [Test]
    public void Funding_rate_does_not_see_a_payment_after_the_close_given_before_it()
    {
        var executor = new GraphExecutor(Graphs.Valid(Rate));
        executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m));
        executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T09:00", rate: 0.0006m));

        IReadOnlyList<CloseResult> at8 = executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T08:00")]);
        IReadOnlyList<CloseResult> at12 = executor.Close(Feed.At("2026-01-01T12:00"), [Feed.Candle("BTCUSDT", "2026-01-01T12:00")]);

        Assert.Multiple(() =>
        {
            Assert.That(Feed.Numbers(at8), Is.EqualTo(new decimal?[] { 0.0001m }));
            Assert.That(Feed.Numbers(at12), Is.EqualTo(new decimal?[] { 0.0006m }));
        });
    }

    [Test]
    public void Funding_rate_is_absent_before_the_first_payment_and_reads_payments_only_of_its_pair()
    {
        List<CloseResult> results = Feed.Run(
            Graphs.Valid(Rate),
            [Feed.Candle("BTCUSDT", "2026-01-01T04:00"), Feed.Candle("ETHUSDT", "2026-01-01T04:00")],
            [Feed.Payment("ETHUSDT", "2026-01-01T00:00", rate: 0.0002m)]);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { null, 0.0002m }));
    }

    [Test]
    public void Funding_rate_on_a_reference_dataset_bar_by_bar()
    {
        List<CloseResult> results = Feed.Run(Graphs.Valid(Rate), Closes, SevenNineFour);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { 7m, 9m, 4m, 4m }));
    }

    [Test]
    public void Previous_on_a_reference_dataset_bar_by_bar()
    {
        List<CloseResult> results = Feed.Run(Graphs.Valid(PreviousRate), Closes, SevenNineFour);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { null, 7m, 9m, 4m }));
    }

    [Test]
    public void Previous_is_absent_when_its_input_was_absent_on_the_previous_close()
    {
        List<CloseResult> results = Feed.Run(
            Graphs.Valid(PreviousRate),
            Closes.Take(3),
            [Feed.Payment("BTCUSDT", "2026-01-01T07:00", rate: 5m)]);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { null, null, 5m }));
    }

    [TestCase("2026-01-01T04:00")]
    [TestCase("2026-01-01T08:00")]
    [TestCase("2026-01-01T12:00")]
    public void Cutting_the_data_at_a_moment_changes_no_value_of_funding_rate_or_previous_before_it(string at)
    {
        DateTimeOffset cut = Feed.At(at);
        foreach (string json in new[] { Rate, PreviousRate })
        {
            CompositeNode graph = Graphs.Valid(json);
            decimal?[] full = Feed.Numbers(Feed.Run(graph, Closes, SevenNineFour).Where(result => result.CloseTime <= cut));
            decimal?[] cutData = Feed.Numbers(Feed.Run(
                graph,
                Closes.Where(candle => candle.CloseTime <= cut),
                SevenNineFour.Where(payment => payment.Time <= cut)));

            Assert.That(cutData, Is.EqualTo(full));
        }
    }

    [TestCase("ge", "0.0005", "0.0005", true)]
    [TestCase("lt", "0.0005", "0.0005", false)]
    [TestCase("ge", "0.0004", "0.0005", false)]
    [TestCase("lt", "0.0004", "0.0005", true)]
    [TestCase("ge", "-0.0006", "-0.0007", true)]
    public void Compare_applies_its_operation(string operation, string a, string b, bool expected)
    {
        string json = Graphs.BooleanOutput(
            $$"""{ "id": "now", "kind": "compare", "settings": { "operation": "{{operation}}" }, "inputs": { "a": {{a}}, "b": {{b}} } }""",
            wires: "",
            "now.result");

        Assert.That(Booleans(json), Is.EqualTo(new[] { expected }));
    }

    [TestCase("ge")]
    [TestCase("lt")]
    public void Compare_with_an_absent_number_is_false(string operation)
    {
        string json = Graphs.BooleanOutput(
            $$"""{ "id": "rate", "kind": "funding-rate" }, { "id": "now", "kind": "compare", "settings": { "operation": "{{operation}}" }, "inputs": { "b": 0.0005 } }""",
            """{ "from": "rate.value", "to": "now.a" }""",
            "now.result");

        Assert.That(Booleans(json), Is.EqualTo(new[] { false }));
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, false)]
    public void And_is_true_only_when_both_inputs_are(bool a, bool b, bool expected)
    {
        string json = Graphs.BooleanOutput(
            $$"""{ "id": "first", "kind": "and", "inputs": { "a": {{(a ? "true" : "false")}}, "b": {{(b ? "true" : "false")}} } }""",
            wires: "",
            "first.result");

        Assert.That(Booleans(json), Is.EqualTo(new[] { expected }));
    }

    // The output value of a graph on one close of a pair with no funding payments.
    private static bool[] Booleans(string json)
    {
        return Feed.Run(Graphs.Valid(json), [Feed.Candle("BTCUSDT", "2026-01-01T04:00")], [])
            .Select(result => result.Outputs["value"].Boolean)
            .ToArray();
    }
}