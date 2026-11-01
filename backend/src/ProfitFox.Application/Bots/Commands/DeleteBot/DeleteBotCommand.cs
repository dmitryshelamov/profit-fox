using MediatR;

namespace ProfitFox.Application.Bots.Commands.DeleteBot;

public sealed record DeleteBotCommand(Guid Id) : IRequest;