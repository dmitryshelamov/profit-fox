using MediatR;

using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Accounts.DeleteAccount;

/// <summary>
///     Removes an account with its encrypted secret (spec exchange-accounts «Удаление аккаунта»); false when there was none.
/// </summary>
public sealed record DeleteAccountCommand(Guid Id) : IRequest<bool>;

internal sealed class DeleteAccountCommandHandler(IExchangeAccountRepository accounts) : IRequestHandler<DeleteAccountCommand, bool>
{
    public Task<bool> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        return accounts.DeleteAsync(request.Id, cancellationToken);
    }
}
