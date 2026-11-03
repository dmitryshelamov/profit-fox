using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class CandleReadThroughTests
{
    private static readonly Instrument Btc = new(MarketCategory.Spot, "BTCUSDT");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private ICandleRepository _candles = null!;
    private IMarketHistoryClient _history = null!;
    private CandleReadThrough _readThrough = null!;
    private List<Candle> _stored = null!;

    [SetUp]
    public void SetUp()
    {
        _stored = [];
        _candles = Substitute.For<ICandleRepository>();
        _history = Substitute.For<IMarketHistoryClient>();
        _candles.GetOpenTimesAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => _stored.Select(candle => candle.OpenTime).Order().ToList());
        _candles.GetAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Candle>)_stored.OrderBy(candle => candle.OpenTime).ToList());
        _readThrough = new CandleReadThrough(_candles, _history, new FakeTimeProvider(Now));
    }

    [Test]
    public async Task Stored_period_does_not_call_the_exchange()
    {
        _stored.AddRange(Hours("2024-03-10T08:00:00Z", "2024-03-10T09:00:00Z", "2024-03-10T10:00:00Z"));

        CandleReadThroughResult result = await ReadAsync("2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z");

        result.Candles.Select(candle => candle.OpenTime).ShouldBe(_stored.Select(candle => candle.OpenTime));
        result.Unavailable.ShouldBeFalse();
        await _history.DidNotReceiveWithAnyArgs().GetCandlesOnceAsync(default!, default, default, default, default);
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default);
    }

    [Test]
    public async Task Missing_parts_are_requested_once_from_the_start_of_the_first_to_the_end_of_the_last()
    {
        _stored.AddRange(Hours("2024-03-10T08:00:00Z", "2024-03-10T10:00:00Z"));
        _history.GetCandlesOnceAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await ReadAsync("2024-03-10T07:00:00Z", "2024-03-10T12:00:00Z");

        await _history.Received(1).GetCandlesOnceAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-03-10T07:00:00Z"),
            DateTimeOffset.Parse("2024-03-10T12:00:00Z"),
            Arg.Any<CancellationToken>());
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default);
    }

    [Test]
    public async Task Only_closed_candles_of_the_missing_parts_are_stored()
    {
        _stored.AddRange(Hours("2024-03-10T09:00:00Z"));
        // 12:00 opens a candle that ends after "now" (12:30), 09:00 is stored, 06:00 lies before the period.
        _history.GetCandlesOnceAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Hours("2024-03-10T12:00:00Z", "2024-03-10T11:00:00Z", "2024-03-10T09:00:00Z", "2024-03-10T08:00:00Z", "2024-03-10T06:00:00Z"));

        await ReadAsync("2024-03-10T08:00:00Z", "2024-03-10T13:00:00Z");

        await _candles.Received(1).UpsertAsync(
            Btc,
            Interval.OneHour,
            Arg.Is<IReadOnlyCollection<Candle>>(saved => saved.Select(candle => candle.OpenTime).Order()
                .SequenceEqual(new[] { DateTimeOffset.Parse("2024-03-10T08:00:00Z"), DateTimeOffset.Parse("2024-03-10T11:00:00Z") })),
            Arg.Any<CancellationToken>());
    }

    // The exchange may still serve a just-closed candle unfinished: it is stored a few seconds after its close, not at once.
    [Test]
    public async Task A_candle_that_closed_a_moment_ago_is_not_stored_yet_but_is_after_five_seconds()
    {
        _history.GetCandlesOnceAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Hours("2024-03-10T11:00:00Z"));
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:00:04Z"));
        _readThrough = new CandleReadThrough(_candles, _history, time);

        await ReadAsync("2024-03-10T11:00:00Z", "2024-03-10T12:00:00Z");
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default);

        time.Advance(TimeSpan.FromSeconds(1));
        await ReadAsync("2024-03-10T11:00:00Z", "2024-03-10T12:00:00Z");

        await _candles.ReceivedWithAnyArgs(1).UpsertAsync(default!, default, default!, default);
    }

    [Test]
    public async Task Exchange_failure_gives_the_stored_candles_flagged_unavailable_and_writes_nothing()
    {
        _stored.AddRange(Hours("2024-03-10T09:00:00Z"));
        _history.GetCandlesOnceAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Candle>>(_ => throw new ExchangeUnavailableException("down"));

        CandleReadThroughResult result = await ReadAsync("2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z");

        result.Unavailable.ShouldBeTrue();
        result.Candles.Select(candle => candle.OpenTime).ShouldBe([DateTimeOffset.Parse("2024-03-10T09:00:00Z")]);
        await _candles.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default!, default);
    }

    private static Candle[] Hours(params string[] openTimes)
    {
        return [.. openTimes.Select(openTime => CandleGenerator.Generate(DateTimeOffset.Parse(openTime)))];
    }

    private Task<CandleReadThroughResult> ReadAsync(string from, string to)
    {
        return _readThrough.ReadAsync(Btc, Interval.OneHour, DateTimeOffset.Parse(from), DateTimeOffset.Parse(to), CancellationToken.None);
    }
}