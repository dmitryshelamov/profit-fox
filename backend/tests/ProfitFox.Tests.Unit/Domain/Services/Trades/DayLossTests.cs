using ProfitFox.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.5: the loss of a day (design.md, decision 11; spec trade-risk «Убыток за день»).
/// </summary>
public sealed class DayLossTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 28, 15, 42, 0, TimeSpan.Zero);

    // Spec «Закрытые и открытые».
    [Test]
    public void Closed_of_minus_20_and_plus_5_and_open_of_minus_12_and_plus_3_lose_24()
    {
        var day = DayLoss.Count([(Now.AddHours(-3), -20m), (Now.AddHours(-1), 5m)], [-12m, 3m], Now, null);
        Assert.That((day.Sum, day.Loss), Is.EqualTo((-24m, 24m)));
    }

    // Spec «Вчерашняя сделка».
    [Test]
    public void A_trade_closed_yesterday_by_UTC_does_not_count_today()
    {
        var day = DayLoss.Count([(new DateTimeOffset(2026, 11, 27, 23, 59, 59, TimeSpan.Zero), -40m)], [], Now, null);
        Assert.That(day.Loss, Is.EqualTo(0m));
    }

    [Test]
    public void The_day_starts_at_midnight_UTC_whatever_the_offset_of_the_moment()
    {
        var late = new DateTimeOffset(2026, 11, 29, 1, 30, 0, TimeSpan.FromHours(7));
        Assert.That(DayLoss.Start(late, null), Is.EqualTo(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(DayLoss.Count([(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero), -1m)], [], late, null).Loss, Is.EqualTo(1m));
    }

    // Spec «Запрет снят»: after the lift the count starts anew.
    [Test]
    public void After_the_lift_only_the_trades_closed_later_count()
    {
        DateTimeOffset lifted = Now.AddHours(-1);
        var day = DayLoss.Count([(Now.AddHours(-2), -52.10m), (Now.AddMinutes(-10), -3m)], [], Now, lifted);
        Assert.That((day.From, day.Loss), Is.EqualTo((lifted, 3m)));
    }

    [Test]
    public void A_lift_of_yesterday_counts_from_midnight()
    {
        Assert.That(DayLoss.Start(Now, Now.AddDays(-1)), Is.EqualTo(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void A_day_in_the_plus_loses_nothing()
    {
        Assert.That(DayLoss.Count([(Now, 10m)], [-2m], Now, null).Loss, Is.EqualTo(0m));
    }
}
