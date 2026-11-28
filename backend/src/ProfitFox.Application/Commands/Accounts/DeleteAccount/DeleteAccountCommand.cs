using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Accounts.DeleteAccount;

/// <summary>
///     Removes an account with its encrypted secret (spec exchange-accounts «Удаление аккаунта»); false when there was none.
///     An account with a trade that holds its pair is not removed (change take-idea-trades, spec idea-trades «Аккаунт со
///     сделкой не удаляется»); its closed and cancelled trades keep its name. The check and the removal are one statement under
///     the advisory lock of the entry.
/// </summary>
public sealed record DeleteAccountCommand(Guid Id) : IRequest<bool>;

internal sealed class DeleteAccountCommandHandler(IExchangeAccountRepository accounts) : IRequestHandler<DeleteAccountCommand, bool>
{
    /// <exception cref="TradeConflictException">A trade of the account holds its pair.</exception>
    public async Task<bool> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        // One removal under the lock of the entry (review R3): an entry at the same moment does not leave a trade without its
        // account.
        return await accounts.DeleteIdleAsync(request.Id, cancellationToken)
               ?? throw new TradeConflictException("У аккаунта есть незакрытая сделка: закройте её, прежде чем удалять аккаунт.");
    }
}
