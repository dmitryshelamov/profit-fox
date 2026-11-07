using System.Globalization;

using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class ScannerPairRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private NpgsqlDataSource _dataSource = null!;
    private ScannerPairRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _store = new ScannerPairRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Saved_pairs_are_read_back_with_their_coverage()
    {
        var sol = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, Now.AddDays(-90), Now, Now.AddMinutes(-5), Now.AddHours(-8), Now.AddDays(-400), 0.001m);
        var btc = new ScannerPair(new Instrument(MarketCategory.Spot, "BTCUSDT"), true);

        await _store.SaveAsync([sol, btc], CancellationToken.None);

        (await _store.GetAllAsync(CancellationToken.None)).OrderBy(pair => pair.Instrument.Symbol).ShouldBe([btc, sol]);
    }

    // Task 2.5.
    [Test]
    public async Task Step_and_minimums_of_the_volume_of_a_pair_are_saved()
    {
        var sol = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, TickSize: 0.001m, QuantityStep: 0.1m, MinQuantity: 0.2m, MinAmount: 5m);
        await _store.SaveAsync([sol], CancellationToken.None);

        (await _store.GetAsync(sol.Instrument, CancellationToken.None)).ShouldBe(sol);
    }

    [Test]
    public async Task Pair_is_read_by_its_instrument()
    {
        var sol = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, TickSize: 0.001m);
        await _store.SaveAsync([sol, new ScannerPair(new Instrument(MarketCategory.Spot, "SOLUSDT"), true)], CancellationToken.None);

        (await _store.GetAsync(sol.Instrument, CancellationToken.None)).ShouldBe(sol);
        (await _store.GetAsync(new Instrument(MarketCategory.Linear, "NOPEUSDT"), CancellationToken.None)).ShouldBeNull();
    }

    // Requirement "Некриптовые пары сканера": the flag is kept and changes with the list.
    [Test]
    public async Task Tradfi_pair_is_read_back_with_its_flag()
    {
        var stock = new ScannerPair(new Instrument(MarketCategory.Linear, "AAPLUSDT"), true, TradFi: true);
        var coin = new ScannerPair(new Instrument(MarketCategory.Linear, "AIOUSDT"), true, TradFi: true);
        await _store.SaveAsync([stock, coin], CancellationToken.None);

        await _store.SaveAsync([coin with { TradFi = false }], CancellationToken.None);

        (await _store.GetAllAsync(CancellationToken.None)).OrderBy(pair => pair.Instrument.Symbol).ShouldBe([stock, coin with { TradFi = false }]);
    }

    // Decision 5: the ideas read only the instruments of the non-crypto pairs.
    [Test]
    public async Task Tradfi_instruments_are_the_non_crypto_pairs_only()
    {
        var stock = new ScannerPair(new Instrument(MarketCategory.Linear, "AAPLUSDT"), true, TradFi: true);
        var xstock = new ScannerPair(new Instrument(MarketCategory.Spot, "TSLAXUSDT"), false, TradFi: true);
        await _store.SaveAsync([stock, xstock, new ScannerPair(new Instrument(MarketCategory.Linear, "BTCUSDT"), true)], CancellationToken.None);

        (await _store.GetTradFiAsync(CancellationToken.None)).ShouldBe([stock.Instrument, xstock.Instrument], ignoreOrder: true);
    }

    // Scenario "Пара снята с торгов": the pair stays with its coverage.
    [Test]
    public async Task Pair_out_of_trading_is_saved_as_not_listed()
    {
        var old = new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), true, Now.AddDays(-90), Now);
        await _store.SaveAsync([old], CancellationToken.None);

        await _store.SaveAsync([old with { Listed = false }], CancellationToken.None);

        (await _store.GetAllAsync(CancellationToken.None)).ShouldBe([old with { Listed = false }]);
    }
}