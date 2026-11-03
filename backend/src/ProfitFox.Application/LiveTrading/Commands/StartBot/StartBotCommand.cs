using MediatR;

namespace ProfitFox.Application.LiveTrading.Commands.StartBot;

public sealed record StartBotCommand(Guid Id) : IRequest;