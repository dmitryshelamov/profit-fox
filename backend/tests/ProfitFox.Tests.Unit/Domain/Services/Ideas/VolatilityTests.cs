using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Условия сетапов": «Высокая волатильность» is ATR / price above the 90th percentile of this ratio of the pair.</summary>
public class VolatilityTests
{
    // After each candle from the 14th: ATR(14) of the candles up to it, at most the last 220, over its close.
    [Test]
    public void Ratio_of_a_candle_is_atr_up_to_it_over_its_close()
    {
        IReadOnlyList<decimal> ratios = Volatility.Ratios(IdeaFixtures.Atr());

        ratios.Count.ShouldBe(17);
        ratios[0].ShouldBe(979m / 280m / 91.88m, 0.0000000001m);
        ratios[^1].ShouldBe(3.6521698977401743024063053484m / 109.80m, 0.0000000001m);
    }

    // The engine measures ATR over the 220 candles of a series, so the ratios do too.
    [Test]
    public void Ratio_after_more_than_220_candles_takes_atr_of_the_last_220()
    {
        IReadOnlyList<IdeaCandle> candles = IdeaFixtures.Zones("zones");

        Volatility.Ratios(candles)[^1].ShouldBe(Atr.Compute([.. candles.TakeLast(220)])!.Value / candles[^1].Close);
        Volatility.Ratios(candles)[^1].ShouldNotBe(Atr.Compute(candles)!.Value / candles[^1].Close);
    }

    // Nearest rank: of 1 … 200 the 180th.
    [Test]
    public void Percentile_is_the_ninetieth_by_nearest_rank()
    {
        Volatility.Percentile([.. Enumerable.Range(1, 200).Reverse().Select(value => (decimal)value)], 0.9m).ShouldBe(180m);
    }

    // Requirement "Перцентили волатильности": the 10th and the 90th of the same 200 ratios, 0.001 … 0.200 shuffled, by nearest rank are
    // the 20th and the 180th.
    [Test]
    public void Tenth_and_ninetieth_percentiles_come_from_the_same_ratios()
    {
        decimal[] ratios = [.. Enumerable.Range(1, 200).Select(value => value * 0.001m).OrderBy(value => (value * 7919m) % 1m)];

        Volatility.Percentile(ratios, 0.1m).ShouldBe(0.020m);
        Volatility.Percentile(ratios, 0.9m).ShouldBe(0.180m);
    }

    [Test]
    public void Fewer_than_a_hundred_ratios_give_no_percentile()
    {
        Volatility.Percentile([.. Enumerable.Range(1, 99).Select(value => (decimal)value)], 0.9m).ShouldBeNull();
        Volatility.Percentile([.. Enumerable.Range(1, 99).Select(value => (decimal)value)], 0.1m).ShouldBeNull();
    }
}