using System.Globalization;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Tests.Shared.Fixtures.TradeFlow;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 8 of collect-trade-flow: the daily archive of trades of Bybit, read as it is downloaded, against a stub on a local port.</summary>
public class BybitTradeArchiveTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);

    private FakeBybitArchiveServer _server = null!;
    private BybitTradeArchive _archive = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitArchiveServer.StartAsync();
        _archive = new BybitTradeArchive(new HttpClient { BaseAddress = new Uri(_server.Address) });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    // The minutes of the reference files are counted by hand, the trade at 12:04:59.999 in 12:04 and the one at 12:05:00.000 in 12:05; the
    // trade of the RPI orders counts as any other (decision 5).
    [TestCase(MarketCategory.Linear, "/trading/SOLUSDT/SOLUSDT2026-11-09.csv.gz", "linear")]
    [TestCase(MarketCategory.Spot, "/spot/SOLUSDT/SOLUSDT_2026-11-09.csv.gz", "spot")]
    public async Task Day_of_a_pair_gives_the_minutes_of_its_trades(MarketCategory category, string path, string fixture)
    {
        _server.Serve(path, TradeFlowFixtures.Day(fixture));
        var minutes = new Dictionary<DateTimeOffset, TradeFlowMinute>();

        bool found = await _archive.ReadDayAsync(
            new Instrument(category, "SOLUSDT"),
            Day,
            trade =>
            {
                var started = TradeFlowMinute.Start(trade);
                minutes[started.OpenTime] = minutes.TryGetValue(started.OpenTime, out TradeFlowMinute? minute) ? minute.Add(trade) : started;
            },
            CancellationToken.None);

        found.ShouldBeTrue();
        minutes.Values.ShouldBe(
        [
            new TradeFlowMinute(Minute, 3m, 3m, 451m, 447m, 2, 1),
            new TradeFlowMinute(Minute.AddMinutes(1), 0.5m, 1m, 75.25m, 150m, 1, 1)
        ],
            ignoreOrder: true);
    }

    [Test]
    public async Task Time_of_a_linear_trade_keeps_its_fraction_of_a_second()
    {
        _server.Serve("/trading/SOLUSDT/SOLUSDT2026-11-09.csv.gz", TradeFlowFixtures.Day("linear"));
        var times = new List<DateTimeOffset>();

        await _archive.ReadDayAsync(new Instrument(MarketCategory.Linear, "SOLUSDT"), Day, trade => times.Add(trade.Time), CancellationToken.None);

        times.ShouldContain(DateTimeOffset.Parse("2026-11-09T12:04:59.999Z", CultureInfo.InvariantCulture));
        times.ShouldContain(DateTimeOffset.Parse("2026-11-09T12:05:10.100Z", CultureInfo.InvariantCulture));
    }

    // Scenario "Пара без файла": the pair did not trade that day, or the archive has not come out yet.
    [Test]
    public async Task Day_without_a_file_is_told()
    {
        var trades = 0;

        bool found = await _archive.ReadDayAsync(new Instrument(MarketCategory.Linear, "NEWUSDT"), Day, _ => trades++, CancellationToken.None);

        found.ShouldBeFalse();
        trades.ShouldBe(0);
    }

    [Test]
    public async Task Archive_that_fails_is_unavailable()
    {
        _server.Status = 503;

        await Should.ThrowAsync<ExchangeUnavailableException>(() =>
            _archive.ReadDayAsync(new Instrument(MarketCategory.Spot, "SOLUSDT"), Day, _ => { }, CancellationToken.None));
    }
}