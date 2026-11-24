using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Change add-ideas-and-market, tasks 3.2 and 3.3: spec strategy-execution «Выход сигнала-состояния» — the value of the
///     boolean output at every closed candle, and the state as a condition of an entry.
/// </summary>
public sealed class SignalStateTests
{
    private static readonly Signal CloseAbove = StrategyGraph.ReadSignal(Graphs.CloseAbove).Value!;

    [Test]
    public void The_state_holds_on_each_of_three_candles()
    {
        var executor = new SignalExecutor(CloseAbove);

        bool?[] values = [.. History.Closes(99, 100, 101, 102, 99).Select(candle => executor.Value("BTCUSDT", candle, lastFunding: null))];

        Assert.That(values, Is.EqualTo(new bool?[] { false, true, true, true, false }));
    }

    [Test]
    public void The_state_is_missing_while_its_inputs_are()
    {
        Signal funding = StrategyGraph.ReadSignal(Graphs.FundingAbove).Value!;
        var executor = new SignalExecutor(funding);
        ClosedCandle first = History.Candle(0, 1);
        ClosedCandle second = History.Candle(1, 1);

        Assert.That(executor.Value("BTCUSDT", first, lastFunding: null), Is.Null, "no payment yet: the rate is missing");
        Assert.That(executor.Value("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.001m)), Is.True);
    }

    [Test]
    public void The_value_of_a_signal_of_an_event_is_whether_the_event_happened()
    {
        var executor = new SignalExecutor(StrategyGraph.ReadSignal(Graphs.H0001).Value!);
        ClosedCandle first = History.Candle(0, 1);
        ClosedCandle second = History.Candle(1, 1);
        ClosedCandle third = History.Candle(2, 1);

        Assert.That(executor.Value("BTCUSDT", first, new FundingPayment(first.CloseTime, 0.0001m)), Is.False);
        Assert.That(executor.Value("BTCUSDT", second, new FundingPayment(second.CloseTime, 0.001m)), Is.True);
        Assert.That(executor.Value("BTCUSDT", third, new FundingPayment(second.CloseTime, 0.001m)), Is.False, "the event is the first time only");
    }

    [Test]
    public void An_event_of_a_signal_state_is_its_turn_to_true()
    {
        var executor = new SignalExecutor(CloseAbove);

        DateTimeOffset?[] events = [.. History.Closes(99, 100, 101, 99, 100).Select(candle => executor.Step("BTCUSDT", candle, lastFunding: null)?.Moment)];

        Assert.That(events, Is.EqualTo(new DateTimeOffset?[] { null, History.Candle(1, 0).CloseTime, null, null, History.Candle(4, 0).CloseTime }));
    }

    /// <summary>
    ///     Spec strategy-execution «Состояние как условие входа».
    /// </summary>
    [Test]
    public void Two_states_joined_by_and_give_an_order_where_both_first_hold_together()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(SignalLibrary.StateStrategy(), SignalLibrary.Set).Value!;
        var executor = new StrategyExecutor(strategy);
        // close ≥ 100 from the second candle, the rate ≥ 0.0005 from the third: both hold first at the third.
        decimal[] closes = [99, 101, 102, 103, 104];
        decimal[] rates = [0.001m, 0.0001m, 0.0006m, 0.0007m, 0.0008m];

        EntrySide?[] entries =
        [
            .. closes.Select((close, index) =>
            {
                ClosedCandle candle = History.Candle(index, close);
                return executor.Step("BTCUSDT", candle, new FundingPayment(candle.CloseTime, rates[index])).Entry;
            })
        ];

        Assert.That(entries, Is.EqualTo(new EntrySide?[] { null, null, EntrySide.Short, null, null }));
    }
}
