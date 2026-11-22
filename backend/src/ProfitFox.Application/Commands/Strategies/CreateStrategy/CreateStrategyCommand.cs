using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Strategies.CreateStrategy;

/// <summary>
///     Writes a new strategy of <see cref="Kind" />; a name that is taken is refused. Without a kind the strategy takes the
///     kind of its graph.
/// </summary>
public sealed record CreateStrategyCommand(
    string Name,
    string? Description,
    JsonElement Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    StrategyKind? Kind = null) : IRequest<StrategyInternal>;
