using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.LiveTrading.Commands.ResumeBot;

public sealed class ResumeBotCommandHandler(ITransactionExecutor executor, ILiveBotRepository repository, TimeProvider time) : IRequestHandler<ResumeBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot is not paused.</exception>
    public Task Handle(ResumeBotCommand request, CancellationToken cancellationToken)
    {
        return LiveRequests.ChangeAsync(executor, repository, time, request.Id, (bot, _) => Task.FromResult(bot.Resume()), cancellationToken);
    }
}