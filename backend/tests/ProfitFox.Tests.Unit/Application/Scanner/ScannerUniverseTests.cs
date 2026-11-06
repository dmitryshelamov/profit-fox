using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Universe;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner;

/// <summary>Requirement "Список пар сканера".</summary>
public class ScannerUniverseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private IMarketDataFeed _feed = null!;
    private IScannerPairRepository _pairs = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _feed = Substitute.For<IMarketDataFeed>();
        _pairs = Substitute.For<IScannerPairRepository>();
        _time = new FakeTimeProvider(Now);
    }

    // Scenario "Пара не в USDT".
    [Test]
    public void Only_trading_usdt_spot_pairs_and_usdt_perpetuals_are_kept()
    {
        ListedInstrument[] listed =
        [
            Spot("BTCUSDT", "BTC", "USDT", trading: true),
            Spot("ETHBTC", "ETH", "BTC", trading: true),
            Spot("OLDUSDT", "OLD", "USDT", trading: false),
            Linear("SOLUSDT", "USDT", trading: true, perpetual: true),
            Linear("BTCPERP", "USDC", trading: true, perpetual: true),
            Linear("BTCUSDT-27DEC24", "USDT", trading: true, perpetual: false),
            Linear("NEWUSDT", "USDT", trading: false, perpetual: true)
        ];

        IReadOnlyList<ListedInstrument> selected = ScannerUniverse.Select(listed, new ScannerSettings());

        selected.Select(instrument => new Instrument(instrument.Category, instrument.Symbol)).ShouldBe([new Instrument(MarketCategory.Spot, "BTCUSDT"), new Instrument(MarketCategory.Linear, "SOLUSDT")]);
    }

    [Test]
    public void Symbols_of_the_settings_narrow_the_list_in_both_categories()
    {
        ListedInstrument[] listed =
        [
            Spot("BTCUSDT", "BTC", "USDT", trading: true),
            Spot("XRPUSDT", "XRP", "USDT", trading: true),
            Linear("BTCUSDT", "USDT", trading: true, perpetual: true),
            Linear("SOLUSDT", "USDT", trading: true, perpetual: true)
        ];

        IReadOnlyList<ListedInstrument> selected = ScannerUniverse.Select(listed, new ScannerSettings { Symbols = ["BTCUSDT"] });

        selected.Select(instrument => new Instrument(instrument.Category, instrument.Symbol)).ShouldBe([new Instrument(MarketCategory.Spot, "BTCUSDT"), new Instrument(MarketCategory.Linear, "BTCUSDT")]);
    }

    // Scenarios "Новая пара" and "Пара снята с торгов".
    [Test]
    public void New_pair_is_added_and_a_vanished_one_is_no_longer_listed()
    {
        var kept = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, Now.AddDays(-90), Now);
        var old = new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), true, Now.AddDays(-90), Now);

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge(
            [kept, old],
            [Linear("SOLUSDT", "USDT", trading: true, perpetual: true), Linear("NEWUSDT", "USDT", trading: true, perpetual: true)]);

        changed.ShouldBe([new ScannerPair(new Instrument(MarketCategory.Linear, "NEWUSDT"), true), old with { Listed = false }], ignoreOrder: true);
    }

    // The history of a new pair starts with its launch, when the exchange tells it.
    [Test]
    public void New_pair_keeps_the_time_it_is_listed_since()
    {
        ListedInstrument listed = Linear("NEWUSDT", "USDT", trading: true, perpetual: true) with { ListedAt = Now.AddDays(-3) };

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge([], [listed]);

        changed.ShouldBe([new ScannerPair(new Instrument(MarketCategory.Linear, "NEWUSDT"), true, ListedAt: Now.AddDays(-3))]);
    }

    // Fix after review: the ideas of a pair go on the step of its price, which the list tells; a pair keeps the step the list last told.
    [Test]
    public void Pair_keeps_the_price_step_the_list_tells()
    {
        var same = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, Now.AddDays(-90), Now, TickSize: 0.01m);
        var changing = new ScannerPair(new Instrument(MarketCategory.Linear, "WIFUSDT"), true, Now.AddDays(-90), Now);
        var untold = new ScannerPair(new Instrument(MarketCategory.Linear, "ETHUSDT"), true, Now.AddDays(-90), Now, TickSize: 0.01m);

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge(
            [same, changing, untold],
            [
                Linear("SOLUSDT", "USDT", trading: true, perpetual: true) with { TickSize = 0.01m },
                Linear("ETHUSDT", "USDT", trading: true, perpetual: true),
                Linear("WIFUSDT", "USDT", trading: true, perpetual: true) with { TickSize = 0.0001m },
                Linear("NEWUSDT", "USDT", trading: true, perpetual: true) with { TickSize = 0.5m }
            ]);

        changed.ShouldBe(
            [changing with { TickSize = 0.0001m }, new ScannerPair(new Instrument(MarketCategory.Linear, "NEWUSDT"), true, TickSize: 0.5m)],
            ignoreOrder: true);
    }

    // Scenario "Признак у известной пары": the list tells anew whether a pair is non-crypto; a pair whose type did not change stays as it is.
    [Test]
    public void Known_pair_takes_the_tradfi_flag_the_list_tells_and_keeps_its_data()
    {
        var stock = new ScannerPair(new Instrument(MarketCategory.Linear, "AAPLUSDT"), true, Now.AddDays(-90), Now, Now, Now, Now.AddDays(-200), 0.01m);
        var gold = new ScannerPair(new Instrument(MarketCategory.Linear, "XAUUSDT"), true, Now.AddDays(-90), Now, TradFi: true);

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge(
            [stock, gold],
            [
                Linear("AAPLUSDT", "USDT", trading: true, perpetual: true) with { TradFi = true },
                Linear("XAUUSDT", "USDT", trading: true, perpetual: true) with { TradFi = true }
            ]);

        changed.ShouldBe([stock with { TradFi = true }]);
    }

    // Scenario "Перпетуал на акцию": a new pair the list tells as a stock is non-crypto.
    [Test]
    public void New_pair_takes_the_tradfi_flag_the_list_tells()
    {
        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge([], [Linear("AAPLUSDT", "USDT", trading: true, perpetual: true) with { TradFi = true }]);

        changed.ShouldBe([new ScannerPair(new Instrument(MarketCategory.Linear, "AAPLUSDT"), true, TradFi: true)]);
    }

    // Task 2.5: the pair keeps the step of the volume and the minimums the list last told, for the paper trades of the autos.
    [Test]
    public void Pair_keeps_the_step_and_the_minimums_of_the_volume_the_list_tells()
    {
        var known = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, Now.AddDays(-90), Now, QuantityStep: 0.1m, MinQuantity: 0.1m, MinAmount: 5m);
        var untold = new ScannerPair(new Instrument(MarketCategory.Linear, "ETHUSDT"), true, Now.AddDays(-90), Now, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: 5m);
        var fresh = new ScannerPair(new Instrument(MarketCategory.Linear, "WIFUSDT"), true, Now.AddDays(-90), Now);

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge(
            [known, untold, fresh],
            [
                Linear("SOLUSDT", "USDT", trading: true, perpetual: true) with { QuantityStep = 0.1m, MinQuantity = 0.1m, MinAmount = 5m },
                Linear("ETHUSDT", "USDT", trading: true, perpetual: true),
                Linear("WIFUSDT", "USDT", trading: true, perpetual: true) with { QuantityStep = 1m, MinQuantity = 1m, MinAmount = 5m },
                Linear("NEWUSDT", "USDT", trading: true, perpetual: true) with { QuantityStep = 1m, MinQuantity = 2m, MinAmount = 6m }
            ]);

        changed.ShouldBe(
            [fresh with { QuantityStep = 1m, MinQuantity = 1m, MinAmount = 5m }, new ScannerPair(new Instrument(MarketCategory.Linear, "NEWUSDT"), true, QuantityStep: 1m, MinQuantity: 2m, MinAmount: 6m)],
            ignoreOrder: true);
    }

    [Test]
    public void Pair_back_in_trading_is_listed_again_with_its_data()
    {
        var back = new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), false, Now.AddDays(-90), Now.AddDays(-2));

        IReadOnlyList<ScannerPair> changed = ScannerUniverse.Merge([back], [Spot("OLDUSDT", "OLD", "USDT", trading: true)]);

        changed.ShouldBe([back with { Listed = true }]);
    }

    [Test]
    public async Task List_is_read_again_only_once_the_refresh_interval_passed()
    {
        _feed.ListInstrumentsAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([Spot("BTCUSDT", "BTC", "USDT", trading: true)]);
        _feed.ListInstrumentsAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([Linear("SOLUSDT", "USDT", trading: true, perpetual: true)]);
        var universe = new ScannerUniverse(_feed, _pairs, new ScannerSettings { UniverseRefresh = TimeSpan.FromHours(1) }, _time);

        IReadOnlyList<ScannerPair>? first = await universe.RefreshAsync([], CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(59));
        IReadOnlyList<ScannerPair>? early = await universe.RefreshAsync([], CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));
        IReadOnlyList<ScannerPair>? due = await universe.RefreshAsync([], CancellationToken.None);

        first.ShouldBe([new ScannerPair(new Instrument(MarketCategory.Spot, "BTCUSDT"), true), new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true)]);
        early.ShouldBeNull();
        due.ShouldNotBeNull();
        await _feed.Received(2).ListInstrumentsAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
        await _pairs.Received(2).SaveAsync(Arg.Any<IReadOnlyCollection<ScannerPair>>(), Arg.Any<CancellationToken>());
    }

    private static ListedInstrument Spot(string symbol, string baseCoin, string quoteCoin, bool trading)
    {
        return new ListedInstrument(MarketCategory.Spot, symbol, baseCoin, quoteCoin, trading);
    }

    private static ListedInstrument Linear(string symbol, string quoteCoin, bool trading, bool perpetual)
    {
        return new ListedInstrument(MarketCategory.Linear, symbol, symbol[..3], quoteCoin, trading, perpetual);
    }
}