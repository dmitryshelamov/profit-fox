using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Accounts;

namespace ProfitFox.Application.Commands.Accounts.RenameAccount;

/// <summary>
///     Gives an account a new name by the rules of a new one (spec exchange-accounts «Переименование аккаунта»); the key, the
///     secret and the UID stay, and Bybit is not called.
/// </summary>
public sealed record RenameAccountCommand(Guid Id, string? Name) : IRequest<ExchangeAccount>;

internal sealed class RenameAccountCommandHandler(IExchangeAccountRepository accounts) : IRequestHandler<RenameAccountCommand, ExchangeAccount>
{
    /// <exception cref="AccountRequestException">The name is refused or taken.</exception>
    /// <exception cref="KeyNotFoundException">There is no account of the id.</exception>
    public async Task<ExchangeAccount> Handle(RenameAccountCommand request, CancellationToken cancellationToken)
    {
        if (AccountRules.CheckName(request.Name) is { } problem)
        {
            throw new AccountRequestException([problem]);
        }

        ExchangeAccount account = await accounts.FindAsync(request.Id, cancellationToken)
                                  ?? throw new KeyNotFoundException($"There is no account {request.Id}.");
        string name = request.Name!.Trim();
        if (!await accounts.RenameAsync(account.Id, name, cancellationToken))
        {
            throw new AccountRequestException([AccountRules.TakenName(name)]);
        }

        return account with { Name = name };
    }
}
