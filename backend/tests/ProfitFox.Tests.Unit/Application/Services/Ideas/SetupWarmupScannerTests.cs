using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-setup-strategies, task 6.1: spec strategy-ideas «Глубина прогрева» — the scanner warms a graph up with 500
///     closed candles of its timeframe, a setting; a window deeper than that is missing until it fills with live candles.
/// </summary>
public sealed class SetupWarmupScannerTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Test]
    public void The_warm_up_is_500_candles_by_default_and_in_the_settings_of_the_Worker()
    {
        string path = Path.Combine(Repository.Root, "backend", "src", "ProfitFox.Worker", "appsettings.json");
        JsonNode settings = JsonNode.Parse(File.ReadAllText(path))!;

        Assert.That(new ScannerSettings().WarmupCandles, Is.EqualTo(500));
        Assert.That((int)settings["Scanner"]!["WarmupCandles"]!, Is.EqualTo(500));
    }

    // «Окно в 200 свечей»: the highest high of the 200 candles before is known at the first live candle after the warm-up.
    [Test]
    public async Task A_window_of_200_candles_is_known_at_the_first_live_candle()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => interval == Hour ? Flat(before, Hour, 600) : [];
        await rig.EnableGraph("new-high", Above(200), Hour);
        await rig.Start();

        await rig.Minutes("ETHUSDT", Ten, 60, close: 101.5m, high: 101.5m, low: 99.5m);
        await rig.At(Ten.AddHours(1).AddSeconds(15));

        Idea idea = rig.Ideas.Ideas.Single();
        Assert.That((idea.Symbol, idea.Moment), Is.EqualTo(("ETHUSDT", Ten.AddHours(1))));
    }

    // «Окно глубже прогрева»: a window of 700 candles of 5m after a warm-up of 500 has no value for 200 more live candles,
    // and no idea comes by it before.
    [Test]
    public async Task A_window_deeper_than_the_warm_up_waits_for_live_candles()
    {
        var rig = new ScannerRig();
        rig.History.Candles["eth"] = (interval, before) => interval == FiveMinutes ? Saw(before, 600) : [];
        await rig.EnableGraph("deep-high", Above(700), FiveMinutes);
        await rig.Start();

        DateTimeOffset open = Ten;
        for (int candle = 0; candle < 205; candle++)
        {
            decimal close = SawClose(500 + candle);
            await rig.Minutes("ETHUSDT", open, 5, close: close, high: close, low: close - 1, open: close - 0.5m);
            open += FiveMinutes;
            await rig.At(open.AddSeconds(15));
        }

        DateTimeOffset full = Ten + (200 * FiveMinutes);
        Assert.That(rig.Ideas.Ideas, Is.Not.Empty, "the window fills with live candles");
        Assert.That(rig.Ideas.Ideas.Min(idea => idea.Moment), Is.GreaterThan(full), "the window of 700 is missing until 200 live candles closed");
    }

    // A strategy that enters long the first time the close is above the highest high of the candles before it, a window of
    // the length given.
    private static string Above(int length)
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "c", "kind": "candle" },
                     { "id": "range", "kind": "window", "settings": { "operation": "max" }, "inputs": { "length": {{length}} } },
                     { "id": "range-prev", "kind": "previous" },
                     { "id": "up", "kind": "compare", "settings": { "operation": "gt" } },
                     { "id": "first", "kind": "first-time" },
                     { "id": "enter", "kind": "enter", "settings": { "side": "long" }, "inputs": { "size": 100 } },
                     { "id": "position", "kind": "position" },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "8h" } }
                   ],
                   "wires": [
                     { "from": "c.high", "to": "range.value" },
                     { "from": "range.result", "to": "range-prev.value" },
                     { "from": "c.close", "to": "up.a" },
                     { "from": "range-prev.result", "to": "up.b" },
                     { "from": "up.result", "to": "first.value" },
                     { "from": "first.event", "to": "enter.when" },
                     { "from": "enter.order", "to": "position.orders" },
                     { "from": "position.position", "to": "close-after.position" },
                     { "from": "close-after.order", "to": "position.orders" }
                   ]
                 }
                 """;
    }

    // Candles at 100 between 99 and 101 up to the warm-up.
    private static IReadOnlyList<HistoryCandle> Flat(DateTimeOffset before, TimeSpan interval, int count)
    {
        List<HistoryCandle> candles = [];
        for (DateTimeOffset open = before - (count * interval); open + interval <= before; open += interval)
        {
            candles.Add(new HistoryCandle(open, "100", "101", "99", "100", "10", Turnover: null, BuyVolume: null));
        }

        return candles;
    }

    // Candles of 5m that rise by turns — a new high every second candle — so a window of highs, once full, makes a close
    // above it every other candle.
    private static IReadOnlyList<HistoryCandle> Saw(DateTimeOffset before, int count)
    {
        List<HistoryCandle> candles = [];
        int index = 500 - count;
        for (DateTimeOffset open = before - (count * FiveMinutes); open + FiveMinutes <= before; open += FiveMinutes)
        {
            string close = Text(SawClose(index));
            string low = Text(SawClose(index) - 1);
            string first = Text(SawClose(index) - 0.5m);
            candles.Add(new HistoryCandle(open, first, close, low, close, "10", Turnover: null, BuyVolume: null));
            index++;
        }

        return candles;
    }

    private static decimal SawClose(int index)
    {
        return 1000 + index - (index % 2 == 1 ? 1.5m : 0);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
