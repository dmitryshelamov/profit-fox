using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 2: the series of an interval warm up from stored minutes joined by <c>time_bucket</c>, the same as the ideas join them.</summary>
public class CandleRepositoryAggregateTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset Ten = DateTimeOffset.Parse("2024-03-01T10:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private CandleRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new CandleRepository(_dataSource);
        await _store.UpsertAsync(Wif, Interval.OneMinute, [.. IdeaFixtures.Minutes().Select(Candle)], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Reference "1 ч из 120 минут с пропусками" of task 1.2.
    [Test]
    public async Task Hours_from_stored_minutes_are_the_reference_hours()
    {
        IReadOnlyList<IdeaCandle> hours = await _store.GetAggregatedAsync(Wif, Interval.OneHour, Ten, Ten.AddHours(2), CancellationToken.None);

        hours.ShouldBe(IdeaCandles.FromMinutes(Interval.OneHour, IdeaFixtures.Minutes(), Ten, Ten.AddHours(2)));
        hours.Count.ShouldBe(2);
    }

    // A candle without trades and one that starts with a gap take the close before them, also from before the period.
    [TestCase(Interval.FiveMinutes, 15, 30)]
    [TestCase(Interval.FifteenMinutes, 60, 120)]
    [TestCase(Interval.FiveMinutes, 0, 120)]
    public async Task Candles_with_gaps_are_the_ones_the_ideas_join(Interval interval, int fromMinute, int toMinute)
    {
        DateTimeOffset from = Ten.AddMinutes(fromMinute);
        DateTimeOffset to = Ten.AddMinutes(toMinute);

        IReadOnlyList<IdeaCandle> candles = await _store.GetAggregatedAsync(Wif, interval, from, to, CancellationToken.None);

        candles.ShouldBe(IdeaCandles.FromMinutes(interval, IdeaFixtures.Minutes(), from, to));
    }

    private static Candle Candle(IdeaCandle minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.High), new Price(minute.Low), new Price(minute.Close), new Volume(1m), new Volume(minute.Volume));
    }
}