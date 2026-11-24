using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-setup-strategies, task 4.7: spec setup-strategies «Метка „Высокая волатильность“» — the signal
///     <c>high-volatility</c> of <c>Fixtures/Setups</c> as the label «↑ волат.» of the scanner on fakes. The fake
///     market-data service of the integration tests has flat prices, so the scanner is driven here, with its warm-up of 500
///     candles (task 6.1): the signal needs 217 candles of 1h.
/// </summary>
public sealed class SetupLabelScannerTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    // «Метка на идее»: the signal is true on the candle of 1h closed at 10:00, and the idea of 15m of 10:30 bears the label.
    // «Идей сигнал не даёт»: SOLUSDT is as volatile and gets no idea from it.
    [Test]
    public async Task An_idea_of_15m_bears_the_label_of_high_volatility_of_the_candle_of_1h_before_it()
    {
        var rig = new ScannerRig(settings: new ScannerSettings { WarmupCandles = 500 });
        foreach (string pair in new[] { "eth", "sol" })
        {
            rig.History.Candles[pair] = (interval, before) => interval == Hour ? Volatile(before) : Warmup(interval, before, "99");
        }

        await rig.Label("high-volatility", "↑ волат.", Hour, Graph());
        await rig.Enable("close-up", "8h", timeframes: [Quarter]);
        await rig.Start();

        await rig.Minutes("ETHUSDT", Ten, 15, close: 99);
        await rig.Minutes("SOLUSDT", Ten, 30, close: 99);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(15), 15, close: 101);
        await rig.At(Ten.AddMinutes(30).AddSeconds(15));

        Idea idea = rig.Ideas.Ideas.Single();
        Assert.That((idea.Symbol, idea.Moment), Is.EqualTo(("ETHUSDT", Ten.AddMinutes(30))));
        Assert.That(idea.Labels, Is.EqualTo(new[] { "↑ волат." }));
    }

    [Test]
    public async Task Without_the_burst_of_range_the_idea_bears_no_label()
    {
        var rig = new ScannerRig(settings: new ScannerSettings { WarmupCandles = 500 });
        rig.History.Candles["eth"] = (interval, before) => interval == Hour ? Volatile(before, burst: false) : Warmup(interval, before, "99");

        await rig.Label("high-volatility", "↑ волат.", Hour, Graph());
        await rig.Enable("close-up", "8h", timeframes: [Quarter]);
        await rig.Start();

        await rig.Minutes("ETHUSDT", Ten, 15, close: 99);
        await rig.Minutes("ETHUSDT", Ten.AddMinutes(15), 15, close: 101);
        await rig.At(Ten.AddMinutes(30).AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Single().Labels, Is.Empty);
    }

    private static string Graph()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Setups", "high-volatility.signal.json");
        return JsonNode.Parse(File.ReadAllText(path))!["graph"]!.ToJsonString();
    }

    // 520 candles of 1h at 100 with a range of 2 up to the close of 10:00; with the burst, the candle from 08:00 to 09:00
    // has a range of 6, so the ATR of the setup of the candle closed at 10:00 is above that of the 200 before.
    private static IReadOnlyList<HistoryCandle> Volatile(DateTimeOffset before, bool burst = true)
    {
        List<HistoryCandle> candles = [];
        for (DateTimeOffset open = Ten.AddHours(-520); open + Hour <= before; open += Hour)
        {
            bool wide = burst && open == Ten.AddHours(-2);
            string high = (wide ? 103m : 101m).ToString(CultureInfo.InvariantCulture);
            string low = (wide ? 97m : 99m).ToString(CultureInfo.InvariantCulture);
            candles.Add(new HistoryCandle(open, "100", high, low, "100", "10", Turnover: null, BuyVolume: null));
        }

        return candles;
    }
}
