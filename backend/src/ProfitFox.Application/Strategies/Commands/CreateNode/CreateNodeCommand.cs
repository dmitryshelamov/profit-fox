using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Commands.CreateNode;

/// <summary>A new custom composite node from its name and JSON definition; returns its id.</summary>
public sealed record CreateNodeCommand(string Name, JsonElement Definition) : IRequest<Guid>;