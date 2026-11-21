using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Strategies.CreateStrategy;

/// <summary>
///     Writes a new strategy; a name that is taken is refused.
/// </summary>
public sealed record CreateStrategyCommand(string Name, string? Description, JsonElement Graph, IReadOnlyDictionary<string, NodePosition> Layout)
    : IRequest<StrategyInternal>;
