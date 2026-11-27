using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.MarketData;

/// <summary>
///     Change add-idea-score, task 6.1 (design.md, decision 7): a candle of history becomes a closed candle with its buy
///     volume, or without one when the history does not know it.
/// </summary>
public sealed class HistoryCandleTests
{
    private static readonly DateTimeOffset Ten = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void The_closed_candle_keeps_the_buy_volume_of_the_history()
    {
        var candle = new HistoryCandle(Ten, "100", "101.5", "99", "100.25", "12.5", Turnover: "1250", BuyVolume: "6.25");

        Assert.That(candle.ToClosedCandle(TimeSpan.FromHours(1)), Is.EqualTo(new ClosedCandle(Ten, Ten.AddHours(1), 100m, 101.5m, 99m, 100.25m, 12.5m, 6.25m)));
    }

    [Test]
    public void A_candle_without_a_known_buy_volume_has_none()
    {
        var candle = new HistoryCandle(Ten, "100", "101.5", "99", "100.25", "12.5", Turnover: null, BuyVolume: null);

        Assert.That(candle.ToClosedCandle(TimeSpan.FromHours(1)).BuyVolume, Is.Null);
    }
}
