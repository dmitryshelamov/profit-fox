using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Strategies.ReplaceStrategy;

/// <summary>
///     Saves a strategy that is there: a graph that differs by content makes its next version.
/// </summary>
public sealed record ReplaceStrategyCommand(string Name, string? Description, JsonElement Graph, IReadOnlyDictionary<string, NodePosition> Layout)
    : IRequest<StrategyInternal>;
