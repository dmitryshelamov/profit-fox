using MediatR;

namespace ProfitFox.Application.LiveTrading.Commands.PauseBot;

public sealed record PauseBotCommand(Guid Id) : IRequest;