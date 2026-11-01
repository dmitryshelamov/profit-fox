using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Backtests;

/// <summary>Requirement "Бот изменился после бэктеста".</summary>
public class BacktestBotChangedTests
{
    private static readonly Backtest Backtest = BacktestGenerator.Generate(BacktestStatus.Succeeded) with
    {
        StrategyVersion = 2,
        Instrument = new Instrument(MarketCategory.Spot, "BTCUSDT"),
        Interval = Interval.OneHour
    };

    private static readonly Bot Bot = new Bot(
        Backtest.BotId,
        "EMA BTC",
        Backtest.StrategyId,
        new Dictionary<string, NodeValue>(),
        Backtest.Instrument,
        Backtest.Interval,
        1000m,
        Backtest.CreatedAt,
        Backtest.CreatedAt);

    [Test]
    public void Bot_with_another_name_values_or_balance_at_the_same_version_has_not_changed()
    {
        Backtest.BotChanged(Bot with { Name = "Другое имя", Balance = 5m, ParameterValues = new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(3m) } }, 2)
            .ShouldBeFalse();
    }

    [Test]
    public void Newer_version_of_the_strategy_changes_the_bot()
    {
        Backtest.BotChanged(Bot, 3).ShouldBeTrue();
    }

    [Test]
    public void Another_instrument_or_interval_changes_the_bot()
    {
        Backtest.BotChanged(Bot with { Instrument = new Instrument(MarketCategory.Spot, "ETHUSDT") }, 2).ShouldBeTrue();
        Backtest.BotChanged(Bot with { Interval = Interval.FourHours }, 2).ShouldBeTrue();
    }
}