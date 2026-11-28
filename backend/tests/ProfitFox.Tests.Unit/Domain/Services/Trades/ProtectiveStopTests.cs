using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.2: the protective stop of a trade (design.md, decision 8; spec trade-risk «Защитный
///     стоп»).
/// </summary>
public sealed class ProtectiveStopTests
{
    // Spec «Стоп идеи ближе предела».
    [Test]
    public void The_stop_of_the_idea_nearer_than_the_limit_is_the_stop()
    {
        Assert.That(ProtectiveStop.Of(TradeSide.Long, 237.40m, 231.38m, 3m, 0.01m), Is.EqualTo((231.38m, StopSource.Idea, 230.28m)));
    }

    // Spec «У идеи нет стопа».
    [Test]
    public void A_short_without_a_stop_gets_the_stop_at_the_limit()
    {
        Assert.That(ProtectiveStop.Of(TradeSide.Short, 1.4126m, null, 3m, 0.0001m), Is.EqualTo((1.4549m, StopSource.Limit, 1.4549m)));
    }

    // Spec «Стоп идеи дальше предела».
    [Test]
    public void The_stop_of_the_idea_beyond_the_limit_gives_way_to_the_limit()
    {
        Assert.That(ProtectiveStop.Of(TradeSide.Long, 100m, 95m, 3m, 0.01m), Is.EqualTo((97m, StopSource.Limit, 97m)));
    }

    [TestCase(TradeSide.Long, 100.003, 2.999)]
    [TestCase(TradeSide.Short, 99.997, 2.999)]
    [TestCase(TradeSide.Long, 77.77, 1.5)]
    [TestCase(TradeSide.Short, 0.012345, 4.2)]
    public void The_rounding_never_takes_the_stop_beyond_the_limit(TradeSide side, double price, double limit)
    {
        (decimal stop, _, _) = ProtectiveStop.Of(side, (decimal)price, null, (decimal)limit, 0.01m);
        Assert.That(((decimal)price - stop) * (int)side / (decimal)price * 100m, Is.LessThanOrEqualTo((decimal)limit));
    }

    [Test]
    public void A_stop_of_the_idea_right_at_the_limit_is_rounded_toward_the_price()
    {
        // 3 % below 100.01 is 97.0097: the nearest step 97.01 is within the limit, 97.00 would not be.
        (decimal stop, StopSource source, _) = ProtectiveStop.Of(TradeSide.Long, 100.01m, 97.0097m, 3m, 0.01m);
        Assert.That((stop, source), Is.EqualTo((97.01m, StopSource.Idea)));
    }

    [Test]
    public void A_stop_of_the_idea_on_the_wrong_side_of_the_price_is_no_stop()
    {
        Assert.That(ProtectiveStop.Of(TradeSide.Short, 100m, 99m, 3m, 0.01m).Source, Is.EqualTo(StopSource.Limit));
    }
}
