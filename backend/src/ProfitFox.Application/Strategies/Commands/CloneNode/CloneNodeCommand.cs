using MediatR;

namespace ProfitFox.Application.Strategies.Commands.CloneNode;

/// <summary>Copies a built-in or custom composite node into a new custom node; without a name the copy is named after the source.</summary>
public sealed record CloneNodeCommand(string Id, string? Name) : IRequest<Guid>;