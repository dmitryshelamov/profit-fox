using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Templates;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Templates;

/// <summary>Requirement «Эталонное поведение шаблонов» for the templates that enter on an indicator.</summary>
public class IndicatorTemplatesTests
{
    private static PlaceOrder Buy(string id)
    {
        return new PlaceOrder(id, "enter", OrderSide.Buy, OrderType.Market, null, null, 100m);
    }

    private static PlaceOrder ExitAll(string id, decimal quantity)
    {
        return new PlaceOrder(id, null, OrderSide.Sell, OrderType.Market, null, quantity, null);
    }

    // Scenario "EMA Crossover": the stop comes before the cross down.
    [Test]
    public void Ema_crossover_buys_on_the_cross_up_and_closes_on_the_stop_before_the_cross_down()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.EmaCrossover, ("fast", 2m), ("slow", 3m));
        decimal[] closes = [10m, 9m, 8m, 7m, 8m, 10m];

        for (int bar = 0; bar < 5; bar++)
        {
            run.Bar(bar, closes[bar]).ShouldBeEmpty();
        }

        run.Bar(5, closes[5]).ShouldBe([Buy("o-1")]);
        run.Fill(5, "o-1", 10m, 10m).ShouldBeEmpty();
        run.Bar(6, 9m).ShouldBe([ExitAll("o-2", 10m)]);
    }

    // Scenario "EMA Crossover": the cross down closes a position the stop has not touched.
    [Test]
    public void Ema_crossover_closes_on_the_cross_down()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.EmaCrossover, ("fast", 2m), ("slow", 3m));
        decimal[] closes = [10m, 9m, 8m, 7m, 8m, 10m, 12m, 12m, 12m, 11m];

        for (int bar = 0; bar < 5; bar++)
        {
            run.Bar(bar, closes[bar]);
        }

        run.Bar(5, closes[5]).ShouldBe([Buy("o-1")]);
        run.Fill(5, "o-1", 10m, 10m);

        for (int bar = 6; bar < 9; bar++)
        {
            run.Bar(bar, closes[bar]).ShouldBeEmpty();
        }

        run.Bar(9, closes[9]).ShouldBe([ExitAll("o-2", 10m)]);
    }

    // Scenario "SMA Crossover": the defaults are 20 and 50 bars.
    [Test]
    public void Sma_crossover_buys_on_the_cross_up_of_the_averages_and_closes_on_the_cross_down()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.SmaCrossover);

        for (int bar = 0; bar < 50; bar++)
        {
            run.Bar(bar, 100m).ShouldBeEmpty();
        }

        run.Bar(50, 200m).ShouldBe([Buy("o-1")]);
        run.Fill(50, "o-1", 0.5m, 200m).ShouldBeEmpty();

        for (int bar = 51; bar < 70; bar++)
        {
            run.Bar(bar, 100m).ShouldBeEmpty();
        }

        run.Bar(70, 100m).ShouldBe([ExitAll("o-2", 0.5m)]);
    }

    // Scenario "MACD по тренду": the line crosses the signal up above the trend average, and down.
    [Test]
    public void Macd_trend_buys_on_the_cross_up_above_the_trend_and_closes_on_the_cross_down()
    {
        StrategyRun run = MacdTrend();
        decimal[] closes = [106m, 92m, 108m, 96m, 90m, 96m, 102m, 98m];

        for (int bar = 0; bar < 5; bar++)
        {
            run.Bar(bar, closes[bar]).ShouldBeEmpty();
        }

        run.Bar(5, closes[5]).ShouldBe([Buy("o-1")]);
        run.Fill(5, "o-1", 1m, 96m);
        run.Bar(6, closes[6]).ShouldBeEmpty();
        run.Bar(7, closes[7]).ShouldBe([ExitAll("o-2", 1m)]);
    }

    // Scenario "MACD против тренда": the only cross up is below the trend average.
    [Test]
    public void Macd_trend_does_not_buy_on_the_cross_up_below_the_trend()
    {
        StrategyRun run = MacdTrend();
        decimal[] closes = [100m, 104m, 92m, 92m, 104m, 104m, 104m, 104m, 98m, 92m, 94m, 92m];

        for (int bar = 0; bar < closes.Length; bar++)
        {
            run.Bar(bar, closes[bar]).ShouldBeEmpty();
        }
    }

    // Scenario "Отскок RSI".
    [Test]
    public void Rsi_rebound_buys_when_the_rsi_rises_over_the_entry_level_and_closes_when_it_reaches_the_exit_level()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.RsiRebound, ("rsiPeriod", 2m));
        decimal[] closes = [96m, 102m, 92m, 104m, 96m, 90m, 98m, 106m];

        for (int bar = 0; bar < 6; bar++)
        {
            run.Bar(bar, closes[bar]).ShouldBeEmpty();
        }

        run.Bar(6, closes[6]).ShouldBe([Buy("o-1")]);
        run.Fill(6, "o-1", 1m, 98m);
        run.Bar(7, closes[7]).ShouldBe([ExitAll("o-2", 1m)]);
    }

    // Scenario "Возврат от полосы Боллинджера": the average of [100, 100, 90, 90] is 95 and its deviation is 5.
    [Test]
    public void Bollinger_return_buys_below_the_lower_band_and_closes_at_the_middle_band()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.BollingerReturn, ("period", 4m), ("width", 0.5m));

        run.Bar(0, 100m).ShouldBeEmpty();
        run.Bar(1, 100m).ShouldBeEmpty();
        run.Bar(2, 90m).ShouldBeEmpty();
        run.Bar(3, 90m).ShouldBe([Buy("o-1")]);
        run.Fill(3, "o-1", 1m, 90m);
        run.Bar(4, 96m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    private static StrategyRun MacdTrend()
    {
        return TemplateRuns.Start(StrategyTemplateKind.MacdTrend, ("fast", 1m), ("slow", 3m), ("signal", 3m), ("trendPeriod", 2m));
    }

    // Scenario "Откуп капитуляции": the position leaves after maxHold bars.
    [Test]
    public void Capitulation_buyback_buys_a_drop_on_volume_and_closes_after_the_bars_it_may_be_held()
    {
        StrategyRun run = Capitulation();
        FlatBars(run, 3);

        run.Bar(3, 93m, open: 100m, volume: 30m).ShouldBe([Buy("o-1")]);
        run.Fill(3, "o-1", 1m, 93m);
        run.Bar(4, 95m).ShouldBeEmpty();
        run.Bar(5, 95m).ShouldBeEmpty();
        run.Bar(6, 95m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    [Test]
    public void Capitulation_buyback_closes_on_the_take_profit()
    {
        StrategyRun run = Capitulation();
        FlatBars(run, 3);
        run.Bar(3, 93m, open: 100m, volume: 30m);
        run.Fill(3, "o-1", 1m, 93m);

        run.Bar(4, 102m).ShouldBeEmpty();
        run.Bar(5, 102.3m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    [Test]
    public void Capitulation_buyback_does_not_buy_a_drop_on_the_volume_of_a_calm_bar()
    {
        StrategyRun run = Capitulation();
        FlatBars(run, 3);

        run.Bar(3, 93m, open: 100m, volume: 19m).ShouldBeEmpty();
    }

    // Scenario "Объём текущего бара не входит в средний": 30 is over twice the 10 of the bars before it, but under twice the average with itself.
    [Test]
    public void The_volume_of_the_current_bar_is_not_in_the_average_it_is_compared_with()
    {
        StrategyRun run = Capitulation();
        FlatBars(run, 3);

        run.Bar(3, 93m, open: 100m, volume: 30m).ShouldBe([Buy("o-1")]);
    }

    // Scenario "Импульс на объёме".
    [Test]
    public void Volume_impulse_buys_a_rise_on_volume_and_closes_on_the_target()
    {
        StrategyRun run = VolumeImpulse();
        FlatBars(run, 3);

        run.Bar(3, 106m, open: 100m, volume: 30m).ShouldBe([Buy("o-1")]);
        run.Fill(3, "o-1", 1m, 106m);
        run.Bar(4, 110m).ShouldBeEmpty();
        run.Bar(5, 116.6m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    [Test]
    public void Volume_impulse_closes_on_the_stop()
    {
        StrategyRun run = VolumeImpulse();
        FlatBars(run, 3);
        run.Bar(3, 106m, open: 100m, volume: 30m);
        run.Fill(3, "o-1", 1m, 106m);

        run.Bar(4, 101m).ShouldBeEmpty();
        run.Bar(5, 100.7m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    [Test]
    public void Volume_impulse_closes_after_the_bars_it_may_be_held()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.VolumeImpulse, ("volumePeriod", 3m), ("maxHold", 2m));
        FlatBars(run, 3);
        run.Bar(3, 106m, open: 100m, volume: 30m);
        run.Fill(3, "o-1", 1m, 106m);

        run.Bar(4, 106m).ShouldBeEmpty();
        run.Bar(5, 106m).ShouldBe([ExitAll("o-2", 1m)]);
    }

    [Test]
    public void Volume_impulse_does_not_buy_a_rise_below_the_impulse()
    {
        StrategyRun run = VolumeImpulse();
        FlatBars(run, 3);

        run.Bar(3, 104m, open: 100m, volume: 30m).ShouldBeEmpty();
    }

    /// <summary>Three bars at 100 with the volume 10.</summary>
    private static void FlatBars(StrategyRun run, int count)
    {
        for (int bar = 0; bar < count; bar++)
        {
            run.Bar(bar, 100m, volume: 10m).ShouldBeEmpty();
        }
    }

    private static StrategyRun Capitulation()
    {
        return TemplateRuns.Start(StrategyTemplateKind.CapitulationBuyback, ("volumePeriod", 3m), ("maxHold", 3m));
    }

    private static StrategyRun VolumeImpulse()
    {
        return TemplateRuns.Start(StrategyTemplateKind.VolumeImpulse, ("volumePeriod", 3m));
    }
}