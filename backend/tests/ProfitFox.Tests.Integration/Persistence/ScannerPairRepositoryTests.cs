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
        _dataSource = await MarketDataDatabase.CreateAsync();
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
        var sol = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, Now.AddDays(-90), Now, Now.AddMinutes(-5), Now.AddHours(-8), Now.AddDays(-400));
        var btc = new ScannerPair(new Instrument(MarketCategory.Spot, "BTCUSDT"), true);

        await _store.SaveAsync([sol, btc], CancellationToken.None);

        (await _store.GetAllAsync(CancellationToken.None)).OrderBy(pair => pair.Instrument.Symbol).ShouldBe([btc, sol]);
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