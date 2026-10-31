using MediatR;

namespace ProfitFox.Application.Strategies.Commands.DeleteNode;

public sealed record DeleteNodeCommand(string Id) : IRequest;