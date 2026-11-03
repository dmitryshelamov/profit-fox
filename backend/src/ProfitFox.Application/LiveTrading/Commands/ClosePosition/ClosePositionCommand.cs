using MediatR;

namespace ProfitFox.Application.LiveTrading.Commands.ClosePosition;

public sealed record ClosePositionCommand(Guid Id) : IRequest;