using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.LiveTrading.Commands.StopBot;

public sealed class StopBotCommandHandler(ITransactionExecutor executor, ILiveBotRepository repository, TimeProvider time) : IRequestHandler<StopBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot is not running or paused.</exception>
    public Task Handle(StopBotCommand request, CancellationToken cancellationToken)
    {
        return LiveRequests.ChangeAsync(executor, repository, time, request.Id, (bot, _) => Task.FromResult(bot.Stop()), cancellationToken);
    }
}