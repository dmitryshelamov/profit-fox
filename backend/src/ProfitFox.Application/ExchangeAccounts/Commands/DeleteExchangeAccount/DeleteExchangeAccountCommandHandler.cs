using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Commands.DeleteExchangeAccount;

public sealed class DeleteExchangeAccountCommandHandler(IExchangeAccountRepository accounts, IStrategyLibraryRepository library) : IRequestHandler<DeleteExchangeAccountCommand>
{
    /// <summary>The account goes with its keys; one that bots trade with stays.</summary>
    /// <exception cref="ExchangeAccountInUseConflictException">Bots trade with the account.</exception>
    /// <exception cref="NotFoundException">There is no such account.</exception>
    public async Task Handle(DeleteExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> bots = await library.GetBotsAsync(cancellationToken);
        List<string> names = [.. bots.Where(bot => bot.ExchangeAccountId == request.Id).Select(bot => bot.Name)];

        if (names.Count > 0)
        {
            throw new ExchangeAccountInUseConflictException(names);
        }

        bool deleted;

        try
        {
            deleted = await accounts.DeleteAsync(request.Id, cancellationToken);
        }
        catch (ExchangeAccountInUseException)
        {
            // A bot took the account after the check above; its name is not known here.
            throw new ExchangeAccountInUseConflictException([]);
        }

        if (!deleted)
        {
            throw new NotFoundException($"There is no exchange account {request.Id}.");
        }
    }
}