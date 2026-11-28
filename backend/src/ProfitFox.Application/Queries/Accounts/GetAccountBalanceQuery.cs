using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Application.Queries.Accounts;

/// <summary>
///     The money of the unified account free for a new position, read at Bybit now (spec exchange-accounts «Баланс по
///     запросу»); it is not kept anywhere.
/// </summary>
public sealed record GetAccountBalanceQuery(Guid Id) : IRequest<decimal>;

/// <summary>
///     The secret is decrypted for the one call of Bybit and let go with it.
/// </summary>
internal sealed class GetAccountBalanceQueryHandler(IExchangeAccountRepository accounts, IExchangeKeyInspector exchange, ISecretProtector protector)
    : IRequestHandler<GetAccountBalanceQuery, decimal>
{
    /// <exception cref="KeyNotFoundException">There is no account of the id.</exception>
    /// <exception cref="SecretUnreadableException">The secret does not decrypt.</exception>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key any more.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused for another reason.</exception>
    public async Task<decimal> Handle(GetAccountBalanceQuery request, CancellationToken cancellationToken)
    {
        ExchangeAccount account = await accounts.FindAsync(request.Id, cancellationToken)
                                  ?? throw new KeyNotFoundException($"There is no account {request.Id}.");
        ProtectedSecret secret = await accounts.SecretAsync(account.Id, cancellationToken)
                                 ?? throw new KeyNotFoundException($"There is no account {request.Id}.");
        return await exchange.FreeAsync(account.ApiKey, protector.Reveal(account.Id, secret), cancellationToken);
    }
}
