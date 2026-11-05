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
    /// <exception cref="ValidationFailedException">An unknown account, or a risk not above zero.</exception>
    /// <exception cref="IdeaTradeRefusedException">The checks, made again under the lock of the account, stop the entry.</exception>
    /// <exception cref="ExchangeException">The exchange did not answer.</exception>
    public async Task<Guid> Handle(CreateIdeaTradeCommand request, CancellationToken cancellationToken)
    {
        EntryPreparation preparation = await calculator.PrepareAsync(request.IdeaId, request.AccountId, request.Risk, cancellationToken)
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
                EntryCalculation calculation = await calculator.EvaluateAsync(preparation, token);

                if (calculation.Reasons.Count > 0)
                {
                    throw new IdeaTradeRefusedException(calculation.Reasons);
                }

                DateTimeOffset now = time.GetUtcNow();
                IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), preparation.Idea, preparation.Account.Id, preparation.Price, calculation.Size, now) with { LastOrderNumber = 1 };
                await trades.CreateAsync(trade, IdeaTradeOrder.NewEntryOf(trade, now), now, token);

                return trade.Id;
            },
            cancellationToken);
    }
}