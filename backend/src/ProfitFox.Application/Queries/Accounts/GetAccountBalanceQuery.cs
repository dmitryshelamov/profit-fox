using MediatR;

using ProfitFox.Application.Contracts.Accounts;
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
public sealed record GetAccountBalanceQuery(Guid Id) : IRequest<AccountBalanceInternal>;

/// <summary>
///     The secret is decrypted for the calls of Bybit and let go with them. The margin mode is read again with the balance and
///     kept in place of the one before: it can be changed at Bybit at any moment (design.md, decision 11).
/// </summary>
internal sealed class GetAccountBalanceQueryHandler(IExchangeAccountRepository accounts, IExchangeKeyInspector exchange, ISecretProtector protector)
    : IRequestHandler<GetAccountBalanceQuery, AccountBalanceInternal>
{
    /// <exception cref="KeyNotFoundException">There is no account of the id.</exception>
    /// <exception cref="SecretUnreadableException">The secret does not decrypt.</exception>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key any more.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused for another reason.</exception>
    public async Task<AccountBalanceInternal> Handle(GetAccountBalanceQuery request, CancellationToken cancellationToken)
    {
        ExchangeAccount account = await accounts.FindAsync(request.Id, cancellationToken)
                                  ?? throw new KeyNotFoundException($"There is no account {request.Id}.");
        ProtectedSecret secret = await accounts.SecretAsync(account.Id, cancellationToken)
                                 ?? throw new KeyNotFoundException($"There is no account {request.Id}.");
        string revealed = protector.Reveal(account.Id, secret);
        decimal free = await exchange.FreeAsync(account.ApiKey, revealed, cancellationToken);
        string? mode = await exchange.MarginModeAsync(account.ApiKey, revealed, cancellationToken);
        await accounts.SetMarginModeAsync(account.Id, mode, cancellationToken);
        return new AccountBalanceInternal(free, mode);
    }
}
