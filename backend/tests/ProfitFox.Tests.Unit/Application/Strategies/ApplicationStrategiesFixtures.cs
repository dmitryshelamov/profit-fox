using ProfitFox.Application.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

namespace ProfitFox.Tests.Unit.Application.Strategies;

internal static class ApplicationStrategiesFixtures
{
    public static StrategyDefinition Ema()
    {
        return StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
    }
}