using MediatR;

namespace ProfitFox.Application.Strategies.Commands.DeleteStrategy;

public sealed record DeleteStrategyCommand(Guid Id) : IRequest;