using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     RSI(14) of Wilder over exactly the last 100 closed candles of <see cref="IdeaFixtures.Rsi" /> (decision 3); the answers are computed in
///     exact fractions.
/// </summary>
public class RsiTests
{
    // The mean of the first 14 of the 99 changes of candles 21 … 120, then smoothed by each next one.
    [Test]
    public void Rsi_is_of_wilder_over_the_last_hundred_candles()
    {
        Rsi.Smooth(IdeaFixtures.Rsi())!.Value!.Value.ShouldBe(65.161470856678374071911934184m, 0.00000000000000000001m);
    }

    // Over all 120 candles from the first one the value would be 65.14996…; the fixed window makes a restart and a replay agree.
    [Test]
    public void Candles_beyond_a_hundred_do_not_change_the_value()
    {
        IReadOnlyList<IdeaCandle> candles = IdeaFixtures.Rsi();

        Rsi.Smooth(candles)!.Value.ShouldBe(Rsi.Smooth([.. candles.TakeLast(100)])!.Value);
    }

    // The closed candles 20 … 119 and the 120th as the next one: one more step of the smoothing.
    [Test]
    public void Rsi_with_the_last_candle_takes_one_step_from_the_closed_ones()
    {
        IReadOnlyList<IdeaCandle> candles = IdeaFixtures.Rsi();

        Rsi.Smooth([.. candles.Take(119)])!.With(candles[^1].Close)!.Value.ShouldBe(65.152641527077100697810260922m, 0.00000000000000000001m);
    }

    [Test]
    public void Fewer_than_a_hundred_candles_give_no_value()
    {
        Rsi.Smooth([.. IdeaFixtures.Rsi().TakeLast(99)]).ShouldBeNull();
    }

    [Test]
    public void Only_gains_give_a_hundred()
    {
        IdeaCandle[] rising = [.. IdeaFixtures.Rsi().Take(100).Select((candle, index) => candle with { Close = 100m + index })];

        Rsi.Smooth(rising)!.Value.ShouldBe(100m);
    }

    // A price that never moved has no strength to measure either way.
    [Test]
    public void A_close_that_never_moves_gives_no_value()
    {
        IdeaCandle[] flat = [.. IdeaFixtures.Rsi().Take(100).Select(candle => candle with { Close = 100m })];

        Rsi.Smooth(flat)!.Value.ShouldBeNull();
        Rsi.Smooth(flat)!.With(100m).ShouldBeNull();
    }
}