using ProfitFox.Application.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Kind"><c>strategy</c> or <c>node</c>.</param>
/// <param name="Id"></param>
/// <param name="Name"></param>
/// <param name="Errors">The errors a node change would cause in this graph, keyed by path.</param>
public sealed record UsageResponse(string Kind, Guid Id, string Name, IReadOnlyDictionary<string, string> Errors)
{
    public static UsageResponse Of(NodeUsage usage)
    {
        return new UsageResponse(StrategyCodes.ToCode(usage.Kind), usage.Id, usage.Name, StrategyRequests.Errors(usage.Errors));
    }
}