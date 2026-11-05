using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>ATR(14) of Wilder on the reference candles of <see cref="IdeaFixtures.Atr" />; the answers are computed in exact fractions.</summary>
public class AtrTests
{
    // The first value is the mean of the first 14 true ranges; the first candle's range is its high minus its low.
    [Test]
    public void Fourteen_candles_give_the_mean_of_their_true_ranges()
    {
        Atr.Compute([.. IdeaFixtures.Atr().Take(14)]).ShouldBe(979m / 280m);
    }

    // Every next candle smooths it: (ATR × 13 + true range) / 14; a gap counts from the close before it.
    [Test]
    public void Later_candles_smooth_the_value_as_wilder_does()
    {
        Atr.Compute(IdeaFixtures.Atr())!.Value.ShouldBe(3.6521698977401743024063053484m, 0.0000000000000000000001m);
    }

    [Test]
    public void Fewer_than_fourteen_candles_give_no_value()
    {
        Atr.Compute([.. IdeaFixtures.Atr().Take(13)]).ShouldBeNull();
    }
}