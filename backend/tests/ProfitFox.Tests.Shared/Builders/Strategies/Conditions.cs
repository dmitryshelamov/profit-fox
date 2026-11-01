using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Tests.Shared.Builders.Strategies;

/// <summary>Trading conditions for engine tests.</summary>
public static class Conditions
{
    /// <summary>The finest steps a decimal holds, no minimum, no fees and a balance no test spends: they never change a command.</summary>
    public static TradingConditions Neutral { get; } = new TradingConditions(
        1_000_000_000_000m,
        new InstrumentRules(0.0000000000000000000000000001m, 0.0000000000000000000000000001m, 0m, 0m),
        new FeeSchedule(0m, 0m, MarketCategory.Linear));

    /// <summary>The instrument of the reference tests: a tick of 0.01, a step and a minimum of 0.001, orders from 1; no fees.</summary>
    public static TradingConditions Reference { get; } = new TradingConditions(
        1000m,
        new InstrumentRules(0.01m, 0.001m, 0.001m, 1m),
        new FeeSchedule(0m, 0m, MarketCategory.Spot));
}