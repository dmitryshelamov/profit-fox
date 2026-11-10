using System.Globalization;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Requirement "Хранение минут потока сделок" and decision 7 of collect-trade-flow.</summary>
public class TradeFlowMinuteRepositoryTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument SpotSol = new Instrument(MarketCategory.Spot, "SOLUSDT");
    private static readonly Guid Owner = Guid.NewGuid();

    private NpgsqlDataSource _dataSource = null!;
    private TradeFlowMinuteRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _store = new TradeFlowMinuteRepository(_dataSource);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE trade_flow_state SET lease_owner = @Owner, leased_until = now() + INTERVAL '1 hour' WHERE id = 1", new { Owner });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Minute_is_read_back_with_the_same_fields()
    {
        var minute = new TradeFlowMinute(Minute, 3.25m, 3m, 451.125m, 447m, 2, 1);

        (await _store.SaveAsync(Owner, [(Sol, minute)], CancellationToken.None)).ShouldBeTrue();

        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Minute, Minute.AddMinutes(1), CancellationToken.None)).ShouldBe([minute]);
    }

    // Scenario "Повторная запись".
    [Test]
    public async Task Minute_written_again_replaces_the_stored_one()
    {
        await _store.SaveAsync(Owner, [(Sol, new TradeFlowMinute(Minute, 3m, 3m, 451m, 447m, 2, 1))], CancellationToken.None);
        var second = new TradeFlowMinute(Minute, 4m, 1m, 600m, 149m, 3, 1);

        await _store.SaveAsync(Owner, [(Sol, second)], CancellationToken.None);

        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Minute, Minute.AddMinutes(1), CancellationToken.None)).ShouldBe([second]);
    }

    // Decision 2: a collector that lost the lease writes nothing.
    [Test]
    public async Task Batch_of_a_collector_without_the_lease_is_not_written()
    {
        bool saved = await _store.SaveAsync(Guid.NewGuid(), [(Sol, new TradeFlowMinute(Minute, 3m, 3m, 451m, 447m, 2, 1))], CancellationToken.None);

        saved.ShouldBeFalse();
        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Minute, Minute.AddMinutes(1), CancellationToken.None)).ShouldBeEmpty();
    }

    // Scenario "Часовые бары": a bar is the sum of the minutes of its candle.
    [Test]
    public async Task Hourly_bars_add_up_their_minutes()
    {
        var hour = DateTimeOffset.Parse("2026-11-09T12:00:00Z", CultureInfo.InvariantCulture);
        (Instrument, TradeFlowMinute)[] minutes =
        [
            .. Enumerable.Range(0, 120).Select(i => (Sol, new TradeFlowMinute(hour.AddMinutes(i), 1m, 0.5m, 100m, 50m, 2, 1))),
            (SpotSol, new TradeFlowMinute(hour, 7m, 7m, 7m, 7m, 7, 7))
        ];
        await _store.SaveAsync(Owner, minutes, CancellationToken.None);

        IReadOnlyList<TradeFlowMinute> bars = await _store.GetBarsAsync(Sol, Interval.OneHour, hour, hour.AddHours(2), CancellationToken.None);

        bars.ShouldBe(
        [
            new TradeFlowMinute(hour, 60m, 30m, 6000m, 3000m, 120, 60),
            new TradeFlowMinute(hour.AddHours(1), 60m, 30m, 6000m, 3000m, 120, 60)
        ]);
    }

    [TestCase(Interval.FiveMinutes, 5)]
    [TestCase(Interval.FifteenMinutes, 15)]
    [TestCase(Interval.FourHours, 240)]
    [TestCase(Interval.OneDay, 1440)]
    public async Task Bar_of_an_interval_starts_on_its_grid(Interval interval, int length)
    {
        await _store.SaveAsync(Owner, [(Sol, new TradeFlowMinute(Day.AddMinutes(length + 1), 1m, 0m, 1m, 0m, 1, 0))], CancellationToken.None);

        IReadOnlyList<TradeFlowMinute> bars = await _store.GetBarsAsync(Sol, interval, Day, Day.AddDays(2), CancellationToken.None);

        bars.Single().OpenTime.ShouldBe(Day.AddMinutes(length));
    }

    // Scenario "Минута без сделок": no row, so no bar.
    [Test]
    public async Task Bar_without_a_minute_is_absent()
    {
        await _store.SaveAsync(
            Owner,
            [(Sol, new TradeFlowMinute(Minute, 1m, 0m, 1m, 0m, 1, 0)), (Sol, new TradeFlowMinute(Minute.AddMinutes(2), 1m, 0m, 1m, 0m, 1, 0))],
            CancellationToken.None);

        IReadOnlyList<TradeFlowMinute> bars = await _store.GetBarsAsync(Sol, Interval.OneMinute, Minute, Minute.AddMinutes(3), CancellationToken.None);

        bars.Select(bar => bar.OpenTime).ShouldBe([Minute, Minute.AddMinutes(2)]);
    }

    [Test]
    public async Task Bars_are_cut_to_the_period()
    {
        await _store.SaveAsync(
            Owner,
            [.. Enumerable.Range(-1, 4).Select(i => (Sol, new TradeFlowMinute(Minute.AddMinutes(i), 1m, 0m, 1m, 0m, 1, 0)))],
            CancellationToken.None);

        IReadOnlyList<TradeFlowMinute> bars = await _store.GetBarsAsync(Sol, Interval.OneMinute, Minute, Minute.AddMinutes(2), CancellationToken.None);

        bars.Select(bar => bar.OpenTime).ShouldBe([Minute, Minute.AddMinutes(1)]);
    }

    // Requirement "Пересборка дня из архива": the minutes of the day are those of the archive, a minute it has not is gone.
    [Test]
    public async Task Day_is_replaced_whole_and_leaves_other_days_and_pairs()
    {
        await _store.SaveAsync(
            Owner,
            [
                (Sol, new TradeFlowMinute(Day.AddMinutes(-1), 1m, 0m, 1m, 0m, 1, 0)),
                (Sol, new TradeFlowMinute(Day.AddMinutes(10), 1m, 0m, 1m, 0m, 1, 0)),
                (Sol, new TradeFlowMinute(Day.AddMinutes(20), 1m, 0m, 1m, 0m, 1, 0)),
                (Sol, new TradeFlowMinute(Day.AddDays(1), 1m, 0m, 1m, 0m, 1, 0)),
                (SpotSol, new TradeFlowMinute(Day.AddMinutes(10), 1m, 0m, 1m, 0m, 1, 0))
            ],
            CancellationToken.None);
        var rebuilt = new TradeFlowMinute(Day.AddMinutes(10), 5m, 5m, 50m, 50m, 5, 5);

        (await _store.ReplaceDayAsync(Owner, Sol, Day, [rebuilt], CancellationToken.None)).ShouldBeTrue();

        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Day.AddDays(-1), Day.AddDays(2), CancellationToken.None)).Select(bar => bar.OpenTime)
            .ShouldBe([Day.AddMinutes(-1), Day.AddMinutes(10), Day.AddDays(1)]);
        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Day.AddMinutes(10), Day.AddMinutes(11), CancellationToken.None)).ShouldBe([rebuilt]);
        (await _store.GetBarsAsync(SpotSol, Interval.OneMinute, Day, Day.AddDays(1), CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Day_of_a_collector_without_the_lease_is_not_replaced()
    {
        await _store.SaveAsync(Owner, [(Sol, new TradeFlowMinute(Day.AddMinutes(10), 1m, 0m, 1m, 0m, 1, 0))], CancellationToken.None);

        bool replaced = await _store.ReplaceDayAsync(Guid.NewGuid(), Sol, Day, [], CancellationToken.None);

        replaced.ShouldBeFalse();
        (await _store.GetBarsAsync(Sol, Interval.OneMinute, Day, Day.AddDays(1), CancellationToken.None)).Count.ShouldBe(1);
    }

    // Scenario "Старые минуты": a chunk older than 7 days is compressed, and its minutes are still read and replaced.
    [Test]
    public async Task Minutes_of_a_compressed_chunk_are_read_and_replaced()
    {
        DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-10);
        var oldDay = new DateTimeOffset(old.Year, old.Month, old.Day, 0, 0, 0, TimeSpan.Zero);
        var minute = new TradeFlowMinute(oldDay.AddMinutes(5), 3m, 3m, 451m, 447m, 2, 1);
        await _store.SaveAsync(Owner, [(Sol, minute), (Sol, new TradeFlowMinute(oldDay.AddMinutes(6), 1m, 1m, 1m, 1m, 1, 1))], CancellationToken.None);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("SELECT compress_chunk(c) FROM show_chunks('trade_flow_minutes', older_than => INTERVAL '7 days') c");
        bool compressed = await connection.QuerySingleAsync<bool>(
            "SELECT bool_and(is_compressed) FROM timescaledb_information.chunks WHERE hypertable_name = 'trade_flow_minutes'");

        IReadOnlyList<TradeFlowMinute> read = await _store.GetBarsAsync(Sol, Interval.OneMinute, minute.OpenTime, minute.OpenTime.AddMinutes(1), CancellationToken.None);
        var rebuilt = new TradeFlowMinute(oldDay.AddMinutes(5), 9m, 9m, 9m, 9m, 9, 9);
        await _store.ReplaceDayAsync(Owner, Sol, oldDay, [rebuilt], CancellationToken.None);
        await _store.SaveAsync(Owner, [(Sol, rebuilt with { BuyTrades = 10 })], CancellationToken.None);

        compressed.ShouldBeTrue();
        read.ShouldBe([minute]);
        (await _store.GetBarsAsync(Sol, Interval.OneMinute, oldDay, oldDay.AddDays(1), CancellationToken.None)).ShouldBe([rebuilt with { BuyTrades = 10 }]);
    }

    [Test]
    public async Task First_minute_is_that_of_the_pair_and_none_without_one()
    {
        await _store.SaveAsync(
            Owner,
            [(Sol, new TradeFlowMinute(Minute.AddMinutes(3), 1m, 0m, 1m, 0m, 1, 0)), (Sol, new TradeFlowMinute(Minute, 1m, 0m, 1m, 0m, 1, 0)), (SpotSol, new TradeFlowMinute(Minute.AddMinutes(-5), 1m, 0m, 1m, 0m, 1, 0))],
            CancellationToken.None);

        (await _store.FirstAsync(Sol, CancellationToken.None)).ShouldBe(Minute);
        (await _store.FirstAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Last_minute_is_the_latest_of_any_pair_and_none_without_one()
    {
        (await _store.LastAsync(CancellationToken.None)).ShouldBeNull();

        await _store.SaveAsync(
            Owner,
            [(Sol, new TradeFlowMinute(Minute, 1m, 0m, 1m, 0m, 1, 0)), (SpotSol, new TradeFlowMinute(Minute.AddMinutes(2), 1m, 0m, 1m, 0m, 1, 0))],
            CancellationToken.None);

        (await _store.LastAsync(CancellationToken.None)).ShouldBe(Minute.AddMinutes(2));
    }
}