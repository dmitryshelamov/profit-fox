using MediatR;

namespace ProfitFox.Application.LiveTrading.Commands.ResumeBot;

public sealed record ResumeBotCommand(Guid Id) : IRequest;