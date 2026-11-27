using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-idea-score, task 5.7: spec market-live-feed «Прогрев метки OI» — the warm-up of a label with the node «OI»
///     takes a point of open interest for each of its 1200 candles, the live candle takes the last point before its close from
///     the feed, and the label on the first live candle is the one a backtest gives on the same data. Only a timeframe with
///     such a graph reads the points.
/// </summary>
public sealed class OpenInterestScannerTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);
    private const int Depth = 1200;

    // «OI ↑ 1h»: the open interest at least 5 % above the one of the candle before.
    private const string OpenInterestUp =
        """
        {
          "formatVersion": 1,
          "outputs": [{ "name": "up", "type": "boolean" }],
          "nodes": [
            { "id": "oi", "kind": "open-interest" },
            { "id": "prev", "kind": "previous" },
            { "id": "ratio", "kind": "divide" },
            { "id": "ge", "kind": "compare", "settings": { "operation": "ge" }, "inputs": { "b": 1.05 } },
            { "id": "out", "kind": "outputs" }
          ],
          "wires": [
            { "from": "oi.value", "to": "prev.value" },
            { "from": "oi.value", "to": "ratio.a" },
            { "from": "prev.result", "to": "ratio.b" },
            { "from": "ratio.result", "to": "ge.a" },
            { "from": "ge.result", "to": "out.up" }
          ]
        }
        """;

    [TestCase(1_060_000, true)]
    [TestCase(1_040_000, false)]
    public async Task The_label_of_open_interest_on_the_first_live_candle_is_that_of_a_backtest(int live, bool put)
    {
        ScannerRig rig = Rig();
        await rig.Label("oi-up-1h", "OI ↑ 1h", Hour, OpenInterestUp);
        await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.OpenInterest, "ETHUSDT", Ten.AddMinutes(30), 1_010_000m), CancellationToken.None);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.OpenInterest, "ETHUSDT", Ten.AddMinutes(55), live), CancellationToken.None);
        await rig.Scanner.ApplyAsync(new LivePoint(LivePointKind.OpenInterest, "ETHUSDT", Eleven, 9_999_999m), CancellationToken.None);
        await rig.At(Eleven.AddSeconds(15));

        Idea idea = rig.Ideas.Ideas.Single(item => item.Symbol == "ETHUSDT");
        bool? backtest = Backtest(rig, live);
        Assert.That(idea.Moment, Is.EqualTo(Eleven));
        Assert.That(backtest, Is.EqualTo(put));
        Assert.That(idea.Labels.Contains("OI ↑ 1h"), Is.EqualTo(backtest));
    }

    [Test]
    public async Task The_warm_up_reads_the_points_once_a_pair_and_only_for_the_timeframe_of_the_node()
    {
        ScannerRig rig = Rig();
        await rig.Label("oi-up-1h", "OI ↑ 1h", Hour, OpenInterestUp);
        await rig.Enable("close-up", "8h", timeframes: [TimeSpan.FromMinutes(15)]);
        await rig.Start();

        Assert.That(rig.History.Asked.Where(asked => asked.StartsWith("open-interest", StringComparison.Ordinal)).Order(), Is.EqualTo(new[] { "open-interest btc", "open-interest eth", "open-interest sol" }));
        Assert.That(rig.History.Asked.Count(asked => asked.StartsWith("candles", StringComparison.Ordinal)), Is.EqualTo(6), "candles of 1h and of 15m for each pair");
    }

    [Test]
    public async Task Graphs_without_the_node_read_no_points()
    {
        ScannerRig rig = Rig();
        await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "8h");
        await rig.Start();

        Assert.That(rig.History.Asked, Has.None.StartsWith("open-interest"));
    }

    // A rig whose warm-up is 1200 candles of 1h closing at 99, with a point of open interest of 1 000 000 every five
    // minutes under them.
    private static ScannerRig Rig()
    {
        var rig = new ScannerRig(settings: new ScannerSettings { WarmupCandles = Depth });
        foreach (Instrument pair in rig.Instruments)
        {
            rig.History.Candles[pair.Id] = (interval, before) => Candles(interval, before);
            rig.History.Points[pair.Id] = [.. Enumerable.Range(0, Depth * 12).Select(step => new OpenInterestPoint(Ten.AddMinutes(5 * (step - (Depth * 12))), 1_000_000m))];
        }

        return rig;
    }

    private static List<HistoryCandle> Candles(TimeSpan interval, DateTimeOffset before)
    {
        var to = new DateTimeOffset(before.UtcTicks / interval.Ticks * interval.Ticks, TimeSpan.Zero);
        return [.. Enumerable.Range(0, Depth).Select(index => new HistoryCandle(to - (interval * (Depth - index)), "99", "100", "98", "99", "10", Turnover: null, BuyVolume: null))];
    }

    // The value of the label at the close of 11:00 as a backtest computes it: the candles of the warm-up and the candle of
    // the minutes told, and every point of open interest — of the history and of the feed — aligned by PairCandles.
    private static bool? Backtest(ScannerRig rig, int live)
    {
        List<HistoryCandle> candles = Candles(Hour, Ten);
        candles.Add(new HistoryCandle(Ten, "100", "101", "100", "101", "60", Turnover: null, BuyVolume: null));
        OpenInterestPoint[] points =
        [
            .. rig.History.Points["eth"],
            new OpenInterestPoint(Ten.AddMinutes(30), 1_010_000m),
            new OpenInterestPoint(Ten.AddMinutes(55), live),
            new OpenInterestPoint(Eleven, 9_999_999m)
        ];
        Signal signal = StrategyGraph.ReadSignal(OpenInterestUp).Value!;
        var executor = new SignalExecutor(signal);
        bool? value = null;
        foreach ((ClosedCandle candle, CandleFacts facts) in PairCandles.Closed(Hour, candles, [], points))
        {
            value = executor.Value("ETHUSDT", candle, facts);
        }

        return value;
    }
}
