using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

public sealed record StrategySummaryResponse(Guid Id, string Name, string Direction)
{
    public static StrategySummaryResponse Of(Strategy strategy)
    {
        return new StrategySummaryResponse(strategy.Id, strategy.Name, StrategyCodes.ToCode(strategy.Definition.Direction));
    }
}