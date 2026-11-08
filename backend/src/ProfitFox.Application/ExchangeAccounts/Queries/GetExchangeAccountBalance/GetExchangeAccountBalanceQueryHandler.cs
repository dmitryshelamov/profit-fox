using System.Security.Cryptography;

using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccountBalance;

public sealed class GetExchangeAccountBalanceQueryHandler(
    IExchangeAccountRepository repository,
    IExchangeKeyInspector inspector,
    IBrokerAccountReader broker,
    ISecretProtector protector)
    : IRequestHandler<GetExchangeAccountBalanceQuery, AccountBalance?>
{
    /// <summary>The balance of a Bybit account from its key, the portfolio of a T-Invest account from its token, in roubles.</summary>
    /// <exception cref="ConflictException">The exchange no longer accepts the key or the token, or the master key changed since the account was added.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    public async Task<AccountBalance?> Handle(GetExchangeAccountBalanceQuery request, CancellationToken cancellationToken)
    {
        ExchangeAccount? account = await repository.GetAsync(request.Id, cancellationToken);
        EncryptedSecret? secret = await repository.GetSecretAsync(request.Id, cancellationToken);

        if (account is null || secret is null)
        {
            return null;
        }

        string plaintext;

        try
        {
            plaintext = protector.Decrypt(account.Id, secret);
        }
        catch (CryptographicException)
        {
            throw new ConflictException("The secret of the account does not decrypt with the current master key; delete the account and add it again.");
        }

        try
        {
            return account.Exchange == Exchange.TInvest
                ? await broker.GetPortfolioAsync(plaintext, account.AccountId!, cancellationToken)
                : await inspector.GetBalanceAsync(account.ApiKey!, plaintext, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            // The key is stored already, so there is no field to fix: the account itself is out of date.
            throw new ConflictException(exception.Message);
        }
    }
}