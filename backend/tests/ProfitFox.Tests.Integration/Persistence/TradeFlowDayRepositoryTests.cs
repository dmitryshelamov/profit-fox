using System.Globalization;

using Npgsql;

using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Requirement "Учёт пересобранных дней" (decision 8 of collect-trade-flow).</summary>
public class TradeFlowDayRepositoryTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-10T03:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument SpotBtc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private NpgsqlDataSource _dataSource = null!;
    private TradeFlowDayRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _store = new TradeFlowDayRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Day_is_read_back_with_its_status_and_time()
    {
        var rebuilt = new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Rebuilt, At);
        var missing = new TradeFlowDay(SpotBtc, Day.AddDays(-1), TradeFlowDayStatus.Missing, At);

        await _store.SaveAsync(rebuilt, CancellationToken.None);
        await _store.SaveAsync(missing, CancellationToken.None);

        (await _store.GetAsync(Day.AddDays(-1), Day.AddDays(1), CancellationToken.None)).ShouldBe([rebuilt, missing], ignoreOrder: true);
    }

    [Test]
    public async Task Day_saved_again_replaces_the_stored_one()
    {
        await _store.SaveAsync(new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Missing, At), CancellationToken.None);
        var again = new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Rebuilt, At.AddHours(1));

        await _store.SaveAsync(again, CancellationToken.None);

        (await _store.GetAsync(Day, Day.AddDays(1), CancellationToken.None)).ShouldBe([again]);
    }

    [Test]
    public async Task Days_are_cut_to_the_window()
    {
        TradeFlowDay[] days = [.. Enumerable.Range(-1, 4).Select(i => new TradeFlowDay(Sol, Day.AddDays(i), TradeFlowDayStatus.Rebuilt, At))];

        foreach (TradeFlowDay day in days)
        {
            await _store.SaveAsync(day, CancellationToken.None);
        }

        (await _store.GetAsync(Day, Day.AddDays(2), CancellationToken.None)).ShouldBe(days[1..3], ignoreOrder: true);
    }

    [Test]
    public async Task Days_of_a_pair_leave_the_other_pairs()
    {
        var sol = new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Rebuilt, At);
        await _store.SaveAsync(sol, CancellationToken.None);
        await _store.SaveAsync(new TradeFlowDay(new Instrument(MarketCategory.Spot, "SOLUSDT"), Day, TradeFlowDayStatus.Rebuilt, At), CancellationToken.None);
        await _store.SaveAsync(new TradeFlowDay(Sol, Day.AddDays(1), TradeFlowDayStatus.Rebuilt, At), CancellationToken.None);

        (await _store.GetAsync(Sol, Day, Day.AddDays(1), CancellationToken.None)).ShouldBe([sol]);
    }
}