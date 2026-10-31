using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Tests.Shared.Builders.Strategies;

public static class StrategyDefinitions
{
    public static StrategyDefinition Long(Graph graph, params StrategyParameter[] parameters)
    {
        return new StrategyDefinition(StrategyDirection.Long, parameters, graph);
    }

    public static StrategyDefinition Short(Graph graph, params StrategyParameter[] parameters)
    {
        return new StrategyDefinition(StrategyDirection.Short, parameters, graph);
    }
}