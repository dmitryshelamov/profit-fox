using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Commands.UpdateNode;

/// <summary>Replaces a custom composite node; the change reaches every graph that uses it.</summary>
public sealed record UpdateNodeCommand(string Id, string Name, JsonElement Definition) : IRequest;