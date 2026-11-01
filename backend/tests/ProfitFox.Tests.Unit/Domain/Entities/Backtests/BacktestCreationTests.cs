using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Backtests;

/// <summary>Requirements of backtest-runs on creating a backtest.</summary>
public class BacktestCreationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly DateTimeOffset To = DateTimeOffset.Parse("2024-02-01T00:00:00Z");
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0.001m, 1m);
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-0000000000ba");

    private static readonly BacktestPolicy Policy = new BacktestPolicy(
        10000,
        new Dictionary<MarketCategory, FeeRates> { [MarketCategory.Spot] = new FeeRates(0.1m, 0.1m), [MarketCategory.Linear] = new FeeRates(0.02m, 0.055m) });

    // Scenario "Бэктест создан".
    [Test]
    public void Backtest_of_a_bot_is_pending_at_the_latest_version_of_its_strategy()
    {
        Backtest backtest = Create(Ema(MarketCategory.Spot), NoSpecs);

        (backtest.Id, backtest.BotId, backtest.StrategyId, backtest.StrategyVersion).ShouldBe((Id, BotId, StrategyId, 3));
        (backtest.Status, backtest.From, backtest.To, backtest.CreatedAt, backtest.TotalCombinations).ShouldBe((BacktestStatus.Pending, From, To, Now, 1L));
        (backtest.Instrument, backtest.Interval, backtest.Rules, backtest.SearchMode).ShouldBe((new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, Rules, SearchMode.Grid));
    }

    // Scenario "Период в будущем".
    [Test]
    public void Period_that_ends_in_the_future_is_rejected()
    {
        Errors(() => Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), From, Now.AddSeconds(1), NoSpecs, NoOverrides, Policy, Rules, Now))
            .ShouldBe([new BacktestError(BacktestField.To, null, "to must not be in the future.")]);
    }

    [Test]
    public void Period_that_ends_inside_a_candle_not_closed_yet_is_rejected()
    {
        // Now is 12:30: the hourly candle of 12:00 closes at 13:00.
        Errors(() => Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), Now.AddHours(-2.5), Now.AddMinutes(-15), NoSpecs, NoOverrides, Policy, Rules, Now))
            .ShouldBe([new BacktestError(BacktestField.To, null, "to must not be later than the end of the last closed candle.")]);
        Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), Now.AddHours(-2.5), Now.AddMinutes(-30), NoSpecs, NoOverrides, Policy, Rules, Now).To.ShouldBe(Now.AddMinutes(-30));
    }

    // Scenario "Некорректный период".
    [TestCase(0)]
    [TestCase(1)]
    public void Period_that_does_not_start_before_it_ends_is_rejected(int days)
    {
        Errors(() => Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), To.AddDays(days), To, NoSpecs, NoOverrides, Policy, Rules, Now))
            .ShouldBe([new BacktestError(BacktestField.Period, null, "from must be earlier than to.")]);
    }

    // Scenario "Диапазон параметра".
    [Test]
    public void Range_gives_its_values_and_other_parameters_take_the_bot_value_or_the_default()
    {
        Backtest backtest = Create(Ema(MarketCategory.Spot), new Dictionary<string, ParameterSpec> { ["fast"] = Range(5m, 15m, 5m) });

        backtest.TotalCombinations.ShouldBe(3);
        backtest.Parameters["fast"].ShouldBe(Range(5m, 15m, 5m));
        backtest.Parameters["slow"].ShouldBe(ParameterSpec.Of(Values.Number(30m)));
        backtest.Parameters["amount"].ShouldBe(ParameterSpec.Of(Values.Number(100m)));
    }

    // Scenarios "Шаг не делит диапазон" and "Диапазон вне ограничений".
    [TestCase(5, 12, 5, "Step must divide the distance from min to max.")]
    [TestCase(1, 10, 1, "Min must be at least 2.")]
    public void Invalid_range_is_rejected_by_parameter(decimal min, decimal max, decimal step, string reason)
    {
        Errors(() => Create(Ema(MarketCategory.Spot), new Dictionary<string, ParameterSpec> { ["fast"] = Range(min, max, step) }))
            .ShouldBe([new BacktestError(BacktestField.Parameter, "fast", reason)]);
    }

    [Test]
    public void Invalid_value_and_an_unknown_parameter_are_rejected_like_values_of_a_bot()
    {
        var specs = new Dictionary<string, ParameterSpec> { ["fast"] = ParameterSpec.Of(Values.Number(1m)), ["medium"] = ParameterSpec.Of(Values.Number(5m)) };

        Errors(() => Create(Ema(MarketCategory.Spot), specs)).ShouldBe(
        [
            new BacktestError(BacktestField.Parameter, "fast", "Value must be at least 2."),
            new BacktestError(BacktestField.Parameter, "medium", "No such parameter.")
        ]);
    }

    // Scenario "Сетка больше лимита".
    [Test]
    public void Grid_above_the_limit_is_rejected_naming_its_size_and_the_limit()
    {
        var specs = new Dictionary<string, ParameterSpec>
        {
            ["fast"] = Range(2m, 31m, 1m),
            ["slow"] = Range(2m, 31m, 1m),
            ["amount"] = Range(1m, 30m, 1m)
        };

        Errors(() => Create(Ema(MarketCategory.Spot), specs))
            .ShouldBe([new BacktestError(BacktestField.Combinations, null, "The grid has 27000 combinations, more than the limit of 10000.")]);
    }

    [Test]
    public void Grid_too_large_to_count_is_rejected()
    {
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        StrategyDefinition wide = ema with { Parameters = [.. ema.Parameters.Select(parameter => parameter with { Min = null, Max = null, Integer = false })] };
        var specs = new Dictionary<string, ParameterSpec>
        {
            ["fast"] = Range(1m, 10_000_000m, 1m),
            ["slow"] = Range(1m, 10_000_000m, 1m),
            ["amount"] = Range(1m, 10_000_000m, 1m)
        };

        Errors(() => Create(Ema(MarketCategory.Spot), specs, wide))
            .ShouldBe([new BacktestError(BacktestField.Combinations, null, "The grid has more combinations than the limit of 10000.")]);
    }

    // Scenario "Значения по умолчанию".
    [Test]
    public void Simulation_without_settings_takes_the_bot_balance_the_bybit_rates_of_the_category_no_slippage_and_breakout()
    {
        Backtest backtest = Create(Ema(MarketCategory.Linear), NoSpecs);

        backtest.Simulation.ShouldBe(new SimulationSettings(1000m, 0.02m, 0.055m, 0m, LimitFillMode.Breakout));
    }

    // Scenario "Переопределённый баланс".
    [Test]
    public void Simulation_takes_the_given_settings()
    {
        Bot bot = Ema(MarketCategory.Spot);

        var backtest = Backtest.Create(Id, bot, Version(), From, To, NoSpecs, new SimulationRequest(500m, 0m, 0.2m, 0.05m, LimitFillMode.Touch), Policy, Rules, Now);

        backtest.Simulation.ShouldBe(new SimulationSettings(500m, 0m, 0.2m, 0.05m, LimitFillMode.Touch));
        bot.Balance.ShouldBe(1000m);
    }

    [Test]
    public void Negative_rates_and_a_balance_that_is_not_positive_are_rejected()
    {
        Errors(() => Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), From, To, NoSpecs, new SimulationRequest(0m, -0.1m, -0.1m, null, null), Policy, Rules, Now)).ShouldBe(
        [
            new BacktestError(BacktestField.Balance, null, "Balance must be greater than zero."),
            new BacktestError(BacktestField.LimitFee, null, "Fee rate must not be negative."),
            new BacktestError(BacktestField.MarketFee, null, "Fee rate must not be negative.")
        ]);
    }

    [TestCase(-0.01)]
    [TestCase(100)]
    public void Slippage_below_zero_or_of_100_percent_and_more_is_rejected(decimal slippage)
    {
        Errors(() => Backtest.Create(Id, Ema(MarketCategory.Spot), Version(), From, To, NoSpecs, new SimulationRequest(null, null, null, slippage, null), Policy, Rules, Now))
            .ShouldBe([new BacktestError(BacktestField.Slippage, null, "Slippage must be from 0 up to 100 percent.")]);
    }

    private static readonly Guid BotId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid StrategyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly IReadOnlyDictionary<string, ParameterSpec> NoSpecs = new Dictionary<string, ParameterSpec>();
    private static readonly SimulationRequest NoOverrides = new SimulationRequest(null, null, null, null, null);

    private static Backtest Create(Bot bot, IReadOnlyDictionary<string, ParameterSpec> specs, StrategyDefinition? definition = null)
    {
        return Backtest.Create(Id, bot, Version(definition), From, To, specs, NoOverrides, Policy, Rules, Now);
    }

    private static IReadOnlyList<BacktestError> Errors(Func<Backtest> create)
    {
        return Should.Throw<BacktestValidationException>(() => create()).Errors;
    }

    /// <summary>Version 3 of EMA, where the default of <c>slow</c> is 30.</summary>
    private static StrategyVersion Version(StrategyDefinition? definition = null)
    {
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        ema = ema with { Parameters = [.. ema.Parameters.Select(parameter => parameter.Name == "slow" ? parameter with { Default = Values.Number(30m) } : parameter)] };

        return new StrategyVersion(StrategyId, 3, StrategySnapshot.Of(definition ?? ema, BuiltInComposites.Catalog), Now);
    }

    private static Bot Ema(MarketCategory category)
    {
        return new Bot(BotId, "EMA BTC", StrategyId, new Dictionary<string, NodeValue> { ["fast"] = Values.Number(10m) }, new Instrument(category, "BTCUSDT"), Interval.OneHour, 1000m, Now, Now);
    }

    private static ParameterSpec Range(decimal min, decimal max, decimal step)
    {
        return ParameterSpec.Between(Values.Number(min), Values.Number(max), Values.Number(step));
    }
}