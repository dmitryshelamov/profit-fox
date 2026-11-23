using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Ideas;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, tasks 9.2 and 9.3: spec idea-labels «Исполнение метки», «Постановка метки на идею»,
///     «Метки идеи не меняются» and «Всплеск» — the labels and the burst in the scanner of ideas on fakes.
/// </summary>
public sealed class IdeaLabelScannerTests
{
    private static readonly DateTimeOffset Eleven = Ten.AddHours(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     «Общая граница»: at one close the label goes before the strategy, and the idea gets the label of that close.
    /// </summary>
    [Test]
    public async Task At_one_close_the_idea_gets_the_label_computed_on_that_close()
    {
        var rig = new ScannerRig();
        await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 99);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Single().Labels, Is.EqualTo(new[] { "↓ BTC" }));
    }

    /// <summary>
    ///     «BTC упал»: an idea of a shorter timeframe gets the label of the last candle of the label closed by its moment.
    /// </summary>
    [Test]
    public async Task An_idea_of_5m_gets_the_label_of_the_last_candle_of_1h_closed_by_its_moment()
    {
        var rig = new ScannerRig();
        await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "8h", timeframes: [FiveMinutes]);
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 99);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 99);
        await rig.At(Eleven.AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven, 5, close: 101);
        await rig.Minutes("ETHUSDT", Eleven, 5, close: 101);
        await rig.At(Eleven.AddMinutes(5).AddSeconds(15));

        Idea idea = rig.Ideas.Ideas.Single(item => item.Symbol == "ETHUSDT");
        Assert.That((idea.Moment, idea.Labels), Is.EqualTo((Eleven.AddMinutes(5), new[] { "↓ BTC" })).Using<(DateTimeOffset, IReadOnlyList<string>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
    }

    /// <summary>
    ///     «Условие не выполнено».
    /// </summary>
    [Test]
    public async Task A_label_whose_signal_is_false_at_its_last_candle_is_not_put()
    {
        var rig = new ScannerRig();
        await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 101);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Labels.Count), Has.All.Zero);
    }

    /// <summary>
    ///     «Метка от события»: the event happened on a candle of 1h before the last one closed by the moment of the idea.
    /// </summary>
    [Test]
    public async Task A_label_of_an_event_is_the_candle_of_the_event_only()
    {
        var rig = new ScannerRig();
        await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100, state: false));
        await rig.Enable("close-up", "8h", timeframes: [FiveMinutes]);
        rig.History.Candles["btc"] = (interval, before) => Warmup(interval, before, "101");
        await rig.Start();

        await rig.Minutes("BTCUSDT", Ten, 60, close: 99);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 99);
        await rig.At(Eleven.AddSeconds(15));
        await rig.Minutes("BTCUSDT", Eleven, 60, close: 99);
        await rig.Minutes("ETHUSDT", Eleven, 60, close: 99);
        await rig.At(Eleven.AddHours(1).AddSeconds(15));
        await rig.Minutes("ETHUSDT", Eleven.AddHours(1), 5, close: 101);
        await rig.At(Eleven.AddHours(1).AddMinutes(5).AddSeconds(15));

        Idea idea = rig.Ideas.Ideas.Single();
        Assert.That(idea.Moment, Is.EqualTo(Eleven.AddHours(1).AddMinutes(5)));
        Assert.That(idea.Labels, Is.Empty, "the event came at 11:00; the last candle of 1h closed by 12:05 is that of 12:00");
    }

    /// <summary>
    ///     «Метку переименовали» and «Новая версия сигнала»: the ideas keep the text they got; after its warm-up the label goes
    ///     by the new version of its signal.
    /// </summary>
    [Test]
    public async Task An_idea_keeps_its_labels_and_a_new_version_of_the_signal_takes_over_after_its_warm_up()
    {
        var rig = new ScannerRig();
        Guid signal = await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "1h");
        await rig.Start();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 99);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));
        Idea first = rig.Ideas.Ideas.Single();

        await rig.Label("btc-drop", "BTC −1 %", Hour, BtcBelow(98), version: 2, id: signal);
        rig.Time.SetUtcNow(Eleven.AddSeconds(30));
        await rig.Warm();
        await rig.Minutes("BTCUSDT", Eleven, 60, close: 99);
        await rig.Minutes("ETHUSDT", Eleven, 60, close: 99);
        await rig.Minutes("SOLUSDT", Eleven, 60, close: 101);
        await rig.At(Eleven.AddHours(1).AddSeconds(15));

        Assert.That(first.Labels, Is.EqualTo(new[] { "↓ BTC" }), "the text is written in the idea");
        Assert.That(rig.Ideas.Ideas.Single(idea => idea.Symbol == "SOLUSDT").Labels, Is.Empty, "version 2 asks the close below 98");
    }

    /// <summary>
    ///     Task 18.14: while a new version of the signal of a label, or its new timeframe, is not warm on a pair, the former
    ///     label goes on being put there, as a former version of a strategy goes on giving ideas.
    /// </summary>
    [TestCase(1)]
    [TestCase(4)]
    public async Task The_former_label_is_put_while_the_new_one_warms_up(int hours)
    {
        var rig = new ScannerRig();
        Guid signal = await rig.Label("btc-drop", "↓ BTC", Hour, BtcBelow(100));
        await rig.Enable("close-up", "8h");
        await rig.Start();

        await rig.Label("btc-drop", "↓ BTC", TimeSpan.FromHours(hours), BtcBelow(100), version: 2, id: signal);
        foreach (ProfitFox.Domain.ValueObjects.MarketData.Instrument pair in Pairs)
        {
            rig.History.Loading.Add(pair.Id);
        }

        rig.Time.SetUtcNow(Ten.AddSeconds(30));
        await rig.Warm();
        await rig.Minutes("BTCUSDT", Ten, 60, close: 99);
        await rig.Minutes("ETHUSDT", Ten, 60, close: 101);
        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas.Single().Labels, Is.EqualTo(new[] { "↓ BTC" }), "version 2 is not warm yet: version 1 puts the label");
    }

    /// <summary>
    ///     «Девять шортов на 1h» and «Семь шортов».
    /// </summary>
    [TestCase(9, true)]
    [TestCase(7, false)]
    public async Task Shorts_of_enough_pairs_on_one_candle_of_1h_are_a_burst(int pairs, bool burst)
    {
        var rig = new ScannerRig(Many(9));
        await rig.Enable("close-up-short", "8h", side: "short");
        await rig.Start();

        foreach (string symbol in rig.Instruments.Take(pairs).Select(pair => pair.Symbol))
        {
            await rig.Minutes(symbol, Ten, 60, close: 101);
        }

        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas, Has.Count.EqualTo(pairs));
        Assert.That(rig.Ideas.Ideas.Select(idea => idea.Labels.Contains(IdeasSettings.BurstLabel)), Has.All.EqualTo(burst));
    }

    /// <summary>
    ///     «Стороны отдельно».
    /// </summary>
    [Test]
    public async Task Five_shorts_and_five_longs_are_no_burst()
    {
        var rig = new ScannerRig(Many(10));
        await rig.Enable("up-long", "8h");
        await rig.Enable("down-short", "8h", side: "short", operation: "lt", level: 98);
        await rig.Start();

        foreach (string symbol in rig.Instruments.Take(5).Select(pair => pair.Symbol))
        {
            await rig.Minutes(symbol, Ten, 60, close: 101);
        }

        foreach (string symbol in rig.Instruments.Skip(5).Select(pair => pair.Symbol))
        {
            await rig.Minutes(symbol, Ten, 60, close: 97);
        }

        await rig.At(Eleven.AddSeconds(15));

        Assert.That(rig.Ideas.Ideas, Has.Count.EqualTo(10));
        Assert.That(rig.Ideas.Ideas.SelectMany(idea => idea.Labels), Is.Empty);
    }

    [Test]
    public void The_thresholds_of_the_burst_are_those_of_v2_by_default()
    {
        var settings = new IdeasSettings();

        Assert.That(
            new[] { "5m", "15m", "1h", "4h", "1d" }.Select(name => settings.Burst[name]),
            Is.EqualTo(new[] { 60, 25, 8, 4, 4 }));
        Assert.That(settings.BurstOf(TimeSpan.FromHours(1)), Is.EqualTo(8));
    }
}
