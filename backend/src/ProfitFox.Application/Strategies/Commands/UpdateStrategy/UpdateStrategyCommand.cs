using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Commands.UpdateStrategy;

/// <summary>Replaces the name and the definition of a strategy.</summary>
public sealed record UpdateStrategyCommand(Guid Id, string Name, JsonElement Definition) : IRequest;