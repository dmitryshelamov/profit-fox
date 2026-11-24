using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 9.4: spec idea-labels «Метки v2 собираются из узлов» — the three labels of v2 as
///     signals-state of the nodes of the catalog.
/// </summary>
public sealed class V2LabelsTests
{
    private static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    // The close at or below 95 % of the close before: «пара упала за сутки на 5 % и больше».
    private const string PairDayDrop =
        """
        {
          "formatVersion": 1,
          "outputs": [{ "name": "dropped", "type": "boolean" }],
          "nodes": [
            { "id": "c", "kind": "candle" },
            { "id": "before", "kind": "previous" },
            { "id": "level", "kind": "multiply", "inputs": { "b": 0.95 } },
            { "id": "low", "kind": "compare", "settings": { "operation": "le" } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "c.close", "to": "before.value" },
            { "from": "before.result", "to": "level.a" },
            { "from": "c.close", "to": "low.a" },
            { "from": "level.result", "to": "low.b" },
            { "from": "low.result", "to": "out.dropped" }
          ]
        }
        """;

    // The close of BTCUSDT at or below 99 % of its close before: «BTC упал за 4 часа на 1 % и больше».
    private const string BtcDrop =
        """
        {
          "formatVersion": 1,
          "outputs": [{ "name": "dropped", "type": "boolean" }],
          "nodes": [
            { "id": "btc", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } },
            { "id": "before", "kind": "previous" },
            { "id": "level", "kind": "multiply", "inputs": { "b": 0.99 } },
            { "id": "low", "kind": "compare", "settings": { "operation": "le" } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "btc.close", "to": "before.value" },
            { "from": "before.result", "to": "level.a" },
            { "from": "btc.close", "to": "low.a" },
            { "from": "level.result", "to": "low.b" },
            { "from": "low.result", "to": "out.dropped" }
          ]
        }
        """;

    // The rate of the last payment at or above 0.05 %: «фандинг 0,05 % и больше».
    private const string HighFunding =
        """
        {
          "formatVersion": 1,
          "outputs": [{ "name": "high", "type": "boolean" }],
          "nodes": [
            { "id": "rate", "kind": "funding-rate" },
            { "id": "ge", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 0.0005 } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [{ "from": "rate.value", "to": "ge.a" }, { "from": "ge.result", "to": "out.high" }]
        }
        """;

    [Test]
    public void The_pair_fell_over_a_day_when_its_close_is_at_or_below_95_percent_of_the_close_before()
    {
        var executor = new SignalExecutor(Signal(PairDayDrop));
        var day = TimeSpan.FromDays(1);

        bool?[] values = [.. new decimal[] { 100, 94, 96, 91.2m }.Select((close, index) => executor.Value("SOLUSDT", Candle(Monday + (index * day), day, close), lastFunding: null))];

        Assert.That(values, Is.EqualTo(new bool?[] { null, true, false, true }), "94 ≤ 95 % of 100; 96 > 95 % of 94; 91.2 = 95 % of 96");
    }

    [Test]
    public void BTC_fell_over_4_hours_on_the_candle_of_any_pair()
    {
        var executor = new SignalExecutor(Signal(BtcDrop));
        var fourHours = TimeSpan.FromHours(4);
        decimal[] btc = [96000, 95040, 95000];

        bool?[] values =
        [
            .. btc.Select((close, index) =>
            {
                DateTimeOffset open = Monday + (index * fourHours);
                var pairs = new Dictionary<string, ClosedCandle> { ["BTCUSDT"] = Candle(open, fourHours, close) };
                return executor.Value("SOLUSDT", Candle(open, fourHours, 238), lastFunding: null, pairs);
            })
        ];

        Assert.That(values, Is.EqualTo(new bool?[] { null, true, false }), "95040 is 99 % of 96000; 95000 is above 99 % of 95040");
    }

    [Test]
    public void The_funding_is_high_while_the_last_payment_is_at_least_005_percent()
    {
        var executor = new SignalExecutor(Signal(HighFunding));
        var hour = TimeSpan.FromHours(1);
        ClosedCandle Hour(int index)
        {
            return Candle(Monday + (index * hour), hour, 1);
        }

        bool?[] values =
        [
            executor.Value("SOLUSDT", Hour(0), new FundingPayment(Monday, 0.0001m)),
            executor.Value("SOLUSDT", Hour(8), new FundingPayment(Monday.AddHours(8), 0.0005m)),
            executor.Value("SOLUSDT", Hour(9), new FundingPayment(Monday.AddHours(8), 0.0005m)),
            executor.Value("SOLUSDT", Hour(16), new FundingPayment(Monday.AddHours(16), 0.00049m))
        ];

        Assert.That(values, Is.EqualTo(new bool?[] { false, true, true, false }));
    }

    private static Signal Signal(string json)
    {
        ReadResult<Signal> read = StrategyGraph.ReadSignal(json);
        Assert.That(read.Errors, Is.Empty, "a signal of the nodes of the catalog");
        Assert.That(read.Value!.IsState, Is.True);
        return read.Value;
    }

    private static ClosedCandle Candle(DateTimeOffset open, TimeSpan length, decimal close)
    {
        return new ClosedCandle(open, open + length, close, close, close, close, 1);
    }
}
