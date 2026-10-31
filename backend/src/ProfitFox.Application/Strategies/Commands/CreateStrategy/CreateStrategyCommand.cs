using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Commands.CreateStrategy;

/// <summary>A new strategy from its name and JSON definition; returns its id.</summary>
public sealed record CreateStrategyCommand(string Name, JsonElement Definition) : IRequest<Guid>;