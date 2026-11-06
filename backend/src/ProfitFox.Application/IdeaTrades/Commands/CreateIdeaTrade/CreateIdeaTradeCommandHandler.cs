using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Commands.CreateIdeaTrade;

public sealed class CreateIdeaTradeCommandHandler(
    IdeaTradeEntryCalculator calculator,
    IIdeaTradeRepository trades,
    ILiveBotRepository live,
    ITransactionExecutor executor,
    TimeProvider time) : IRequestHandler<CreateIdeaTradeCommand, Guid>
{
    /// <exception cref="NotFoundException">There is no such idea.</exception>
    /// <exception cref="ValidationFailedException">An unknown account, or an amount not above zero.</exception>
    /// <exception cref="IdeaTradeRefusedException">The checks, made again under the lock of the account, stop the entry.</exception>
    /// <exception cref="ExchangeException">The exchange did not answer.</exception>
    public async Task<Guid> Handle(CreateIdeaTradeCommand request, CancellationToken cancellationToken)
    {
        EntryPreparation preparation = await calculator.PrepareAsync(request.IdeaId, request.AccountId, request.Amount, cancellationToken)
                                       ?? throw new NotFoundException($"There is no idea {request.IdeaId}.");

        if (preparation.Account is null)
        {
            throw new IdeaTradeRefusedException((await calculator.EvaluateAsync(preparation, cancellationToken)).Reasons);
        }

        return await executor.ExecuteAsync(
            async token =>
            {
                // The money and the open trades of the account are checked for one entry or start at a time.
                await live.LockAccountAsync(preparation.Account.Id, token);

                // The limits of open trades and of the total risk count every account: the entries of all of them go one at a time, after the bots.
                await trades.LockCreationAsync(token);
                EntryCalculation calculation = await calculator.EvaluateAsync(preparation, token);

                if (calculation.Reasons.Count > 0)
                {
                    throw new IdeaTradeRefusedException(calculation.Reasons);
                }

                DateTimeOffset now = time.GetUtcNow();
                IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), preparation.Idea, preparation.Account.Id, preparation.Price, calculation.Size, now, calculation.Promise) with { LastOrderNumber = 1 };
                await trades.CreateAsync(trade, IdeaTradeOrder.NewEntryOf(trade, now), now, token);

                return trade.Id;
            },
            cancellationToken);
    }
}