using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Execution;

[TestFixture]
public class GraphExecutorTests
{
    // The previous close's funding rate of a pair: shows what the state of the pair holds.
    private static readonly string PreviousRate = Graphs.NumberOutput(
        """{ "id": "rate", "kind": "funding-rate" }, { "id": "prev", "kind": "previous" }""",
        """{ "from": "rate.value", "to": "prev.value" }""",
        "prev.result");

    [Test]
    public void Each_candle_of_a_moment_gets_its_result()
    {
        var executor = new GraphExecutor(Graphs.Valid(Graphs.H0001));

        IReadOnlyList<CloseResult> results = executor.Close(
            Feed.At("2026-01-01T04:00"),
            [Feed.Candle("BTCUSDT", "2026-01-01T04:00"), Feed.Candle("ETHUSDT", "2026-01-01T04:00")]);

        Assert.That(
            results.Select(result => (result.InstrumentId, result.CloseTime)),
            Is.EqualTo(new[] { ("BTCUSDT", Feed.At("2026-01-01T04:00")), ("ETHUSDT", Feed.At("2026-01-01T04:00")) }));
    }

    [Test]
    public void A_pair_without_a_candle_in_a_moment_gets_no_result_and_keeps_its_state()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));
        executor.AddFundingPayment(Feed.Payment("ETHUSDT", "2026-01-01T00:00", rate: 0.0001m));
        executor.Close(Feed.At("2026-01-01T04:00"), [Feed.Candle("BTCUSDT", "2026-01-01T04:00"), Feed.Candle("ETHUSDT", "2026-01-01T04:00")]);
        executor.AddFundingPayment(Feed.Payment("ETHUSDT", "2026-01-01T08:00", rate: 0.0003m));

        IReadOnlyList<CloseResult> at8 = executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T08:00")]);
        IReadOnlyList<CloseResult> at12 = executor.Close(Feed.At("2026-01-01T12:00"), [Feed.Candle("ETHUSDT", "2026-01-01T12:00")]);

        Assert.Multiple(() =>
        {
            Assert.That(at8.Select(result => result.InstrumentId), Is.EqualTo(new[] { "BTCUSDT" }));
            Assert.That(Feed.Numbers(at12), Is.EqualTo(new decimal?[] { 0.0001m }));
        });
    }

    [Test]
    public void A_close_not_after_the_last_one_is_rejected_and_changes_nothing()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));
        executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m));
        executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T08:00")]);

        Assert.Throws<ArgumentException>(() => executor.Close(Feed.At("2026-01-01T04:00"), [Feed.Candle("BTCUSDT", "2026-01-01T04:00")]));
        Assert.Throws<ArgumentException>(() => executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T08:00")]));
        IReadOnlyList<CloseResult> next = executor.Close(Feed.At("2026-01-01T12:00"), [Feed.Candle("BTCUSDT", "2026-01-01T12:00")]);

        Assert.That(Feed.Numbers(next), Is.EqualTo(new decimal?[] { 0.0001m }));
    }

    [Test]
    public void A_moment_with_a_candle_closing_at_another_time_is_rejected()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));

        Assert.Throws<ArgumentException>(() => executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T04:00")]));
    }

    [Test]
    public void A_moment_with_two_candles_of_one_pair_is_rejected()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));

        Assert.Throws<ArgumentException>(() => executor.Close(
            Feed.At("2026-01-01T04:00"),
            [Feed.Candle("BTCUSDT", "2026-01-01T04:00"), Feed.Candle("BTCUSDT", "2026-01-01T04:00")]));
    }

    [Test]
    public void A_payment_at_or_before_a_computed_close_is_rejected()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));
        executor.Close(Feed.At("2026-01-01T08:00"), [Feed.Candle("BTCUSDT", "2026-01-01T08:00")]);

        Assert.Throws<ArgumentException>(() => executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T08:00", rate: 0.0006m)));
    }

    [Test]
    public void A_payment_before_the_last_payment_of_its_pair_is_rejected()
    {
        var executor = new GraphExecutor(Graphs.Valid(PreviousRate));
        executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T08:00", rate: 0.0001m));

        Assert.Throws<ArgumentException>(() => executor.AddFundingPayment(Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m)));
    }

    [Test]
    public void The_previous_close_of_a_pair_is_its_previous_candle_given_not_a_timeframe_earlier()
    {
        CompositeNode graph = Graphs.Valid(PreviousRate);

        List<CloseResult> results = Feed.Run(
            graph,
            [Feed.Candle("BTCUSDT", "2026-01-01T04:00"), Feed.Candle("BTCUSDT", "2026-01-01T12:00")],
            [Feed.Payment("BTCUSDT", "2026-01-01T00:00", rate: 0.0001m), Feed.Payment("BTCUSDT", "2026-01-01T08:00", rate: 0.0002m)]);

        Assert.That(Feed.Numbers(results), Is.EqualTo(new decimal?[] { null, 0.0001m }));
    }

    [Test]
    public void Two_runs_on_the_same_data_give_the_same_results()
    {
        CompositeNode graph = Graphs.Valid(Graphs.H0001);
        var fixture = ParityFixture.Load();

        List<CloseResult> first = Feed.Run(graph, fixture.Candles, fixture.Payments);
        List<CloseResult> second = Feed.Run(graph, fixture.Candles, fixture.Payments);

        Assert.That(
            second.Select(result => (result.InstrumentId, result.CloseTime, result.Outputs["entry"])),
            Is.EqualTo(first.Select(result => (result.InstrumentId, result.CloseTime, result.Outputs["entry"]))));
    }

    [Test]
    public void Data_after_a_moment_does_not_change_the_results_up_to_it()
    {
        CompositeNode graph = Graphs.Valid(Graphs.H0001);
        var fixture = ParityFixture.Load();
        DateTimeOffset cut = fixture.Candles[fixture.Candles.Count / 2].CloseTime;

        List<CloseResult> full = Feed.Run(graph, fixture.Candles, fixture.Payments);
        List<CloseResult> cutData = Feed.Run(
            graph,
            fixture.Candles.Where(candle => candle.CloseTime <= cut),
            fixture.Payments.Where(payment => payment.Time <= cut));

        Assert.That(cutData, Is.Not.Empty);
        Assert.That(
            cutData.Select(result => (result.InstrumentId, result.CloseTime, result.Outputs["entry"])),
            Is.EqualTo(full.Where(result => result.CloseTime <= cut).Select(result => (result.InstrumentId, result.CloseTime, result.Outputs["entry"]))));
    }
}