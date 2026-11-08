using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

/// <summary>Decision 4 and requirement "Догрузка при чтении истории графика" on a market with sessions.</summary>
public class CandleReadThroughCoverageTests
{
    private static readonly Instrument Sber = new(MarketCategory.Moex, "SBER");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly DateTimeOffset Monday = DateTimeOffset.Parse("2024-03-04T00:00:00Z");

    private ICandleRepository _candles = null!;
    private ISeriesCoverageRepository _coverage = null!;
    private IMarketHistoryClient _history = null!;
    private CandleReadThrough _readThrough = null!;
    private List<Candle> _stored = null!;

    [SetUp]
    public void SetUp()
    {
        _stored = [];
        _candles = Substitute.For<ICandleRepository>();
        _candles.GetOpenTimesAsync(Sber, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => _stored.Select(candle => candle.OpenTime).Order().ToList());
        _candles.GetAsync(Sber, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Candle>)_stored.OrderBy(candle => candle.OpenTime).ToList());
        _coverage = Substitute.For<ISeriesCoverageRepository>();
        _history = Substitute.For<IMarketHistoryClient>();
        _history.GetCandlesOnceAsync(Sber, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _readThrough = new CandleReadThrough(_candles, _coverage, _history, new FakeTimeProvider(Now));
    }

    // Scenario "Ночь MOEX не догружается".
    [Test]
    public async Task Week_inside_the_coverage_with_nights_and_a_weekend_asks_tinvest_for_nothing()
    {
        foreach (int day in Enumerable.Range(0, 5))
        {
            _stored.AddRange(Enumerable.Range(7, 14).Select(hour => CandleGenerator.Generate(Monday.AddDays(day).AddHours(hour))));
        }

        _coverage.GetAsync(Sber, Interval.OneHour, Arg.Any<CancellationToken>()).Returns(new SeriesCoverage(Monday, Monday.AddDays(7)));

        CandleReadThroughResult result = await _readThrough.ReadAsync(Sber, Interval.OneHour, Monday, Monday.AddDays(7), CancellationToken.None);

        result.Candles.Count.ShouldBe(70);
        result.Unavailable.ShouldBeFalse();
        _history.ReceivedCalls().ShouldBeEmpty();
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default(SeriesCoverage)!, default);
    }

    // The first read of a series asks for the whole period and makes its coverage up to the end of the period.
    [Test]
    public async Task First_read_asks_for_the_whole_period_and_covers_it()
    {
        _history.GetCandlesOnceAsync(Sber, Interval.OneHour, Monday, Monday.AddDays(1), Arg.Any<CancellationToken>())
            .Returns([CandleGenerator.Generate(Monday.AddHours(20)), CandleGenerator.Generate(Monday.AddHours(7))]);

        await _readThrough.ReadAsync(Sber, Interval.OneHour, Monday, Monday.AddDays(1), CancellationToken.None);

        await _candles.Received(1).UpsertAsync(
            Sber,
            Interval.OneHour,
            Arg.Is<IReadOnlyCollection<Candle>>(saved => saved.Count == 2),
            new SeriesCoverage(Monday, Monday.AddDays(1)),
            Arg.Any<CancellationToken>());
    }

    // Only the part outside the coverage is asked for, and the border moves on, even when the exchange gives no candles (a weekend).
    [Test]
    public async Task Part_outside_the_coverage_is_asked_for_and_the_border_moves_even_without_candles()
    {
        _stored.Add(CandleGenerator.Generate(Monday.AddDays(4).AddHours(20)));
        _coverage.GetAsync(Sber, Interval.OneHour, Arg.Any<CancellationToken>()).Returns(new SeriesCoverage(Monday, Monday.AddDays(5)));

        await _readThrough.ReadAsync(Sber, Interval.OneHour, Monday.AddDays(4), Monday.AddDays(6), CancellationToken.None);

        await _history.Received(1).GetCandlesOnceAsync(Sber, Interval.OneHour, Monday.AddDays(5), Monday.AddDays(6), Arg.Any<CancellationToken>());
        await _candles.Received(1).UpsertAsync(Sber, Interval.OneHour, Arg.Is<IReadOnlyCollection<Candle>>(saved => saved.Count == 0), new SeriesCoverage(Monday, Monday.AddDays(6)), Arg.Any<CancellationToken>());
    }

    // Scenario "Граница не заходит в будущее".
    [Test]
    public async Task Border_stops_at_the_last_closed_candle()
    {
        var today = DateTimeOffset.Parse("2024-03-10T00:00:00Z");

        await _readThrough.ReadAsync(Sber, Interval.OneHour, today.AddHours(8), today.AddHours(13), CancellationToken.None);

        await _candles.Received(1).UpsertAsync(Sber, Interval.OneHour, Arg.Any<IReadOnlyCollection<Candle>>(), new SeriesCoverage(today.AddHours(8), today.AddHours(12)), Arg.Any<CancellationToken>());
    }

    // After the review: the border grows over the whole period asked for, not over the gaps alone, so the candles stored between two
    // gaps do not keep the gaps apart from the coverage. Inside the period everything is stored, covered or has just been asked for.
    [Test]
    public async Task Period_touching_the_coverage_through_stored_candles_is_covered_whole()
    {
        DateTimeOffset tuesday = Monday.AddDays(1);
        _stored.AddRange(Enumerable.Range(12, 8).Select(hour => CandleGenerator.Generate(Monday.AddHours(hour))));
        _stored.AddRange(Enumerable.Range(7, 5).Select(hour => CandleGenerator.Generate(tuesday.AddHours(hour))));
        _coverage.GetAsync(Sber, Interval.OneHour, Arg.Any<CancellationToken>()).Returns(new SeriesCoverage(Monday, Monday.AddHours(12)));

        await _readThrough.ReadAsync(Sber, Interval.OneHour, Monday.AddHours(12), tuesday.AddHours(12), CancellationToken.None);

        await _history.Received(1).GetCandlesOnceAsync(Sber, Interval.OneHour, Monday.AddHours(20), tuesday.AddHours(7), Arg.Any<CancellationToken>());
        await _candles.Received(1).UpsertAsync(Sber, Interval.OneHour, Arg.Any<IReadOnlyCollection<Candle>>(), new SeriesCoverage(Monday, tuesday.AddHours(12)), Arg.Any<CancellationToken>());
    }

    // After the review: the exchange may still serve the candle that has just closed as unfinished, and the adapter drops such a candle; a
    // border past it would never ask for it again. The border keeps a minute behind the clock on a market with sessions.
    [Test]
    public async Task Border_keeps_a_minute_behind_the_clock()
    {
        var today = DateTimeOffset.Parse("2024-03-10T00:00:00Z");
        var readThrough = new CandleReadThrough(_candles, _coverage, _history, new FakeTimeProvider(today.AddHours(12).AddSeconds(30)));

        await readThrough.ReadAsync(Sber, Interval.OneHour, today.AddHours(8), today.AddHours(13), CancellationToken.None);

        await _candles.Received(1).UpsertAsync(Sber, Interval.OneHour, Arg.Any<IReadOnlyCollection<Candle>>(), new SeriesCoverage(today.AddHours(8), today.AddHours(11)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Period_inside_the_current_candle_leaves_the_coverage_alone()
    {
        var today = DateTimeOffset.Parse("2024-03-10T00:00:00Z");

        await _readThrough.ReadAsync(Sber, Interval.OneHour, today.AddHours(12), today.AddHours(13), CancellationToken.None);

        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default(SeriesCoverage)!, default);
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default);
    }
}