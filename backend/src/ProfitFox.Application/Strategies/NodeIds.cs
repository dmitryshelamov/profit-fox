using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>Node ids of the API: a built-in code such as <c>ladder</c> or the <see cref="Guid" /> of a custom node.</summary>
internal static class NodeIds
{
    /// <exception cref="StateConflictException">The id names a built-in node, which cannot be changed.</exception>
    /// <exception cref="NotFoundException">The id names no node.</exception>
    public static Guid Custom(string id)
    {
        StrategyJson.TryParseNodeId(id, out CompositeNodeId? parsed);

        return parsed switch
        {
            CustomNodeId custom => custom.Value,
            BuiltInNodeId => throw new StateConflictException("Built-in nodes cannot be changed or deleted. Clone the node and change the clone.", []),
            _ => throw NotFound(id)
        };
    }

    /// <exception cref="StateConflictException">A graph that uses the node stands in the way.</exception>
    public static T InUse<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (NodeInUseException exception)
        {
            throw new StateConflictException("Graphs that use the node would break.", exception.Usages);
        }
    }

    public static NotFoundException NotFound(string id)
    {
        return new NotFoundException($"There is no node {id}.");
    }
}