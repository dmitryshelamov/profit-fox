using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.LiveTrading.Commands.PauseBot;

public sealed class PauseBotCommandHandler(ITransactionExecutor executor, ILiveBotRepository repository, TimeProvider time) : IRequestHandler<PauseBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot is not running.</exception>
    public Task Handle(PauseBotCommand request, CancellationToken cancellationToken)
    {
        return LiveRequests.ChangeAsync(executor, repository, time, request.Id, (bot, _) => Task.FromResult(bot.Pause()), cancellationToken);
    }
}