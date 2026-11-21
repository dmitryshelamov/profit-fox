using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Spec strategy-execution: running at closed candles, pairs apart, the same result every time.
/// </summary>
public sealed class ExecutionTests
{
    private static readonly Signal H0001 = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

    [Test]
    public void An_event_happens_at_the_close_of_its_candle()
    {
        var executor = new SignalExecutor(H0001);
        ClosedCandle first = History.Candle(index: 1, close: 1);
        ClosedCandle second = History.Candle(index: 2, close: 1);

        SignalEvent? before = executor.Step("BTCUSDT", first, new FundingPayment(first.CloseTime, 0.0001m));
        SignalEvent? after = executor.Step("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.0006m));

        Assert.That(before, Is.Null);
        Assert.That(second.OpenTime, Is.EqualTo(History.Start.AddHours(8)));
        Assert.That(after, Is.EqualTo(new SignalEvent("BTCUSDT", History.Start.AddHours(12))));
    }

    [Test]
    public void Pairs_keep_their_own_state()
    {
        string json = History.WithOutput("number", "prev.result", """
                                                                  { "id": "c", "kind": "candle" }, { "id": "prev", "kind": "previous" }
                                                                  """, """{ "from": "c.close", "to": "prev.value" }""");
        var btc = new GraphRun(History.Graph(json));
        var eth = new GraphRun(History.Graph(json));

        btc.Step(History.Candle(0, 100), lastFunding: null);
        eth.Step(History.Candle(0, 7), lastFunding: null);
        btc.Step(History.Candle(1, 101), lastFunding: null);
        PinValue ethPrevious = eth.Step(History.Candle(1, 8), lastFunding: null)[0];

        Assert.That(ethPrevious.Number, Is.EqualTo(7m));
    }

    [Test]
    public void The_order_of_pairs_does_not_change_the_events()
    {
        (string, ClosedCandle, FundingPayment)[] steps = [.. Enumerable.Range(0, 6).SelectMany(index => new[]
        {
            ("BTCUSDT", History.Candle(index, 1), new FundingPayment(History.Candle(index, 1).CloseTime, index % 2 == 0 ? 0.0001m : 0.0007m)),
            ("ETHUSDT", History.Candle(index, 1), new FundingPayment(History.Candle(index, 1).CloseTime, index % 3 == 0 ? 0.0001m : 0.0009m))
        })];

        List<SignalEvent> inOrder = Events(steps);
        List<SignalEvent> reversed = Events(steps.OrderBy(step => step.Item1 == "BTCUSDT" ? 1 : 0).ToArray());

        Assert.That(reversed, Is.EquivalentTo(inOrder));
        Assert.That(inOrder, Is.Not.Empty);
    }

    [Test]
    public void The_same_history_gives_the_same_events()
    {
        (string, ClosedCandle, FundingPayment)[] steps = [.. Enumerable.Range(0, 20).Select(index =>
            ("BTCUSDT", History.Candle(index, 1), new FundingPayment(History.Candle(index, 1).CloseTime, index % 4 == 1 ? 0.0006m : 0.0002m)))];

        List<SignalEvent> first = Events(steps);
        List<SignalEvent> second = Events(steps);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Is.Not.Empty);
    }

    [Test]
    public void Candles_of_a_pair_go_in_the_order_of_their_close()
    {
        var executor = new SignalExecutor(H0001);
        executor.Step("BTCUSDT", History.Candle(1, 1), lastFunding: null);

        Assert.That(() => executor.Step("BTCUSDT", History.Candle(0, 1), lastFunding: null), Throws.ArgumentException);
    }

    [Test]
    public void A_payment_after_the_close_is_refused()
    {
        var executor = new SignalExecutor(H0001);
        ClosedCandle candle = History.Candle(0, 1);

        Assert.That(
            () => executor.Step("BTCUSDT", candle, new FundingPayment(candle.CloseTime + TimeSpan.FromMinutes(1), 0.001m)),
            Throws.ArgumentException);
    }

    private static List<SignalEvent> Events(IEnumerable<(string Instrument, ClosedCandle Candle, FundingPayment Funding)> steps)
    {
        var executor = new SignalExecutor(H0001);
        return [.. steps.Select(step => executor.Step(step.Instrument, step.Candle, step.Funding)).OfType<SignalEvent>()];
    }
}
