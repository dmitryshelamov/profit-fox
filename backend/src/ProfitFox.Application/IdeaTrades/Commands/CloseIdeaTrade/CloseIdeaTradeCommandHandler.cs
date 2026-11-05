using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Commands.CloseIdeaTrade;

public sealed class CloseIdeaTradeCommandHandler(IIdeaTradeRepository trades, ITransactionExecutor executor, TimeProvider time) : IRequestHandler<CloseIdeaTradeCommand>
{
    /// <exception cref="NotFoundException">There is no such trade.</exception>
    /// <exception cref="ConflictException">The status of the trade does not allow it.</exception>
    public Task Handle(CloseIdeaTradeCommand request, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            async token =>
            {
                IdeaTrade trade = await trades.LockAsync(request.Id, token) ?? throw new NotFoundException($"There is no trade {request.Id}.");

                try
                {
                    await trades.SaveAsync(trade.RequestClose(), time.GetUtcNow(), token);
                }
                catch (IdeaTradeConflictException exception)
                {
                    throw new ConflictException(exception.Message);
                }

                return 0;
            },
            cancellationToken);
    }
}