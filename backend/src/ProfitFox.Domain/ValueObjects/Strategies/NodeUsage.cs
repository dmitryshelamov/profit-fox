using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A strategy or composite node that uses a composite node, with the errors a change of that node would cause in it.</summary>
public sealed record NodeUsage(UsageKind Kind, Guid Id, string Name, IReadOnlyList<GraphError> Errors);