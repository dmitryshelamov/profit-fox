using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.LiveTrading.Commands.ClosePosition;

public sealed class ClosePositionCommandHandler(ITransactionExecutor executor, ILiveBotRepository repository, TimeProvider time) : IRequestHandler<ClosePositionCommand>
{
    /// <summary>Asks a Worker to close the position at the market; the bot stays stopped.</summary>
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot works, or it has no open position.</exception>
    public Task Handle(ClosePositionCommand request, CancellationToken cancellationToken)
    {
        return LiveRequests.ChangeAsync(
            executor,
            repository,
            time,
            request.Id,
            async (bot, token) =>
            {
                LivePosition? current = await repository.GetCurrentPositionAsync(bot.BotId, token);

                return bot.RequestClose(current is { Open: true });
            },
            cancellationToken);
    }
}