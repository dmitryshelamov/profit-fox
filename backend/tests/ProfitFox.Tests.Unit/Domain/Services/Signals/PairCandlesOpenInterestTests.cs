using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Domain.Services.Signals;

/// <summary>
///     Change add-idea-score, task 4.3: spec strategy-execution «Без заглядывания вперёд» — a candle sees the last point of
///     open interest strictly before its close; a point at the close belongs to the next candle, and a candle whose interval
///     has no point keeps the point of an interval before.
/// </summary>
public sealed class PairCandlesOpenInterestTests
{
    private static readonly DateTimeOffset Eleven = new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Test]
    public void A_point_at_the_close_belongs_to_the_next_candle()
    {
        var before = new OpenInterestPoint(Eleven.AddMinutes(55), 4_900_000m);
        var atClose = new OpenInterestPoint(Eleven.AddHours(1), 5_000_000m);
        var later = new OpenInterestPoint(Eleven.AddHours(1).AddMinutes(55), 5_100_000m);

        CandleFacts[] facts = Facts([Candle(Eleven), Candle(Eleven.AddHours(1))], [later, atClose, before]);

        Assert.That(facts.Select(fact => fact.OpenInterest), Is.EqualTo(new[] { before, later }));
    }

    [Test]
    public void A_candle_without_a_point_in_its_interval_keeps_the_point_before()
    {
        var first = new OpenInterestPoint(Eleven.AddMinutes(30), 1m);

        CandleFacts[] facts = Facts([Candle(Eleven), Candle(Eleven.AddHours(1)), Candle(Eleven.AddHours(2))], [first]);

        Assert.That(facts.Select(fact => fact.OpenInterest), Is.EqualTo(new[] { first, first, first }));
    }

    [Test]
    public void Without_points_every_candle_has_none_and_the_payments_go_as_before()
    {
        var payment = new HistoryPayment(Eleven.AddMinutes(10), "0.0001");

        CandleFacts[] facts = [.. PairCandles.Closed(Hour, [Candle(Eleven)], [payment]).Select(closed => closed.Facts)];

        Assert.That(facts.Single().OpenInterest, Is.Null);
        Assert.That(facts.Single().Funding, Is.EqualTo(payment.ToFundingPayment()));
    }

    private static CandleFacts[] Facts(HistoryCandle[] candles, OpenInterestPoint[] points)
    {
        return [.. PairCandles.Closed(Hour, candles, [], points).Select(closed => closed.Facts)];
    }

    private static HistoryCandle Candle(DateTimeOffset open)
    {
        return new HistoryCandle(open, "100", "101", "99", "100", "5", Turnover: null, BuyVolume: null);
    }
}
