using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Strategies.ReplaceStrategy;

/// <summary>
///     Saves a strategy that is there: a graph that differs by content makes its next version. The graph is checked by the
///     rules of the kind of the strategy; a <see cref="Kind" /> other than that is refused.
/// </summary>
public sealed record ReplaceStrategyCommand(
    string Name,
    string? Description,
    JsonElement Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    StrategyKind? Kind = null) : IRequest<StrategyInternal>;
