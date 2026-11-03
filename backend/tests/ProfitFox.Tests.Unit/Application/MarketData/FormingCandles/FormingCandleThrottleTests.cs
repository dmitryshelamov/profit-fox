using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData.FormingCandles;

/// <summary>Requirement "Частота обновлений текущей свечи".</summary>
public class FormingCandleThrottleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly DateTimeOffset Hour = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private FakeTimeProvider _time = null!;
    private List<FormingCandle> _sent = null!;
    private FormingCandleThrottle _throttle = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _sent = [];
        _throttle = new FormingCandleThrottle(_time, _sent.Add);
    }

    [TearDown]
    public void TearDown()
    {
        _throttle.Dispose();
    }

    [Test]
    public void The_first_update_goes_at_once()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 0));

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m]);
    }

    // Scenario "Пять обновлений за секунду".
    [Test]
    public void Five_updates_in_a_second_send_one_and_the_next_second_carries_the_last()
    {
        for (var i = 0; i < 5; i++)
        {
            _throttle.Offer(Update(Hour, 100m + i, seconds: i));
            _time.Advance(TimeSpan.FromMilliseconds(150));
        }

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m]);

        _time.Advance(TimeSpan.FromMilliseconds(250));

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m, 104m]);
    }

    [Test]
    public void An_update_a_second_after_the_last_goes_at_once()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 0));
        _time.Advance(TimeSpan.FromSeconds(1));

        _throttle.Offer(Update(Hour, 101m, seconds: 1));

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m, 101m]);
    }

    [Test]
    public void The_closing_goes_at_once_and_drops_the_held_update()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 0));
        _throttle.Offer(Update(Hour, 101m, seconds: 1));

        _throttle.Offer(Update(Hour, 102m, seconds: 2, closed: true));
        _time.Advance(TimeSpan.FromSeconds(2));

        _sent.Select(candle => (candle.Close.Value, candle.IsClosed)).ShouldBe([(100m, false), (102m, true)]);
    }

    // Scenario "Новый интервал".
    [Test]
    public void The_first_candle_of_the_next_interval_goes_at_once()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 0));
        _time.Advance(TimeSpan.FromMilliseconds(100));

        _throttle.Offer(Update(Hour.AddHours(1), 105m, seconds: 1));

        _sent.Select(candle => candle.OpenTime).ShouldBe([Hour, Hour.AddHours(1)]);
    }

    [Test]
    public void An_update_older_than_the_sent_one_is_dropped()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 5));
        _time.Advance(TimeSpan.FromSeconds(2));

        _throttle.Offer(Update(Hour, 99m, seconds: 4));
        _throttle.Offer(Update(Hour.AddHours(-1), 98m, seconds: 6));
        _time.Advance(TimeSpan.FromSeconds(2));

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m]);
    }

    [Test]
    public void An_update_older_than_the_held_one_does_not_replace_it()
    {
        _throttle.Offer(Update(Hour, 100m, seconds: 0));
        _throttle.Offer(Update(Hour, 102m, seconds: 2));

        _throttle.Offer(Update(Hour, 101m, seconds: 1));
        _time.Advance(TimeSpan.FromSeconds(1));

        _sent.Select(candle => candle.Close.Value).ShouldBe([100m, 102m]);
    }

    [Test]
    public void Latest_is_the_newest_update_taken_sent_or_held()
    {
        _throttle.Latest.ShouldBeNull();
        _throttle.Offer(Update(Hour, 100m, seconds: 0));
        _throttle.Offer(Update(Hour, 101m, seconds: 1));

        _throttle.Latest!.Close.Value.ShouldBe(101m);
    }

    private static FormingCandle Update(DateTimeOffset openTime, decimal close, int seconds, bool closed = false)
    {
        return new FormingCandle(openTime, new Price(close), new Price(close), new Price(close), new Price(close), new Volume(1m), closed, openTime.AddMinutes(30).AddSeconds(seconds));
    }
}