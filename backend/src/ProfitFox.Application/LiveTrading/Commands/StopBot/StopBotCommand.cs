using MediatR;

namespace ProfitFox.Application.LiveTrading.Commands.StopBot;

public sealed record StopBotCommand(Guid Id) : IRequest;