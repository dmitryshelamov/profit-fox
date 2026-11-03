using System.Security.Cryptography;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Application.LiveTrading;

/// <summary>What the commands of live trading share.</summary>
internal static class LiveRequests
{
    /// <summary>Changes the live bot under the lock of the bot; the change sees the books of the bot in the same unit of work.</summary>
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The status of the bot does not allow the change.</exception>
    public static Task ChangeAsync(
        ITransactionExecutor executor,
        ILiveBotRepository repository,
        TimeProvider time,
        Guid id,
        Func<LiveBot, CancellationToken, Task<LiveBot>> change,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            async token =>
            {
                DateTimeOffset now = time.GetUtcNow();
                LiveBot bot = await repository.LockAsync(id, now, token) ?? throw new NotFoundException($"There is no bot {id}.");

                try
                {
                    await repository.SaveAsync(await change(bot, token), now, token);
                }
                catch (LiveBotConflictException exception)
                {
                    throw new ConflictException(exception.Message);
                }

                return 0;
            },
            cancellationToken);
    }

    /// <summary>The decrypted key of the account, for the calls of one action.</summary>
    /// <exception cref="ConflictException">The account is gone, or its secret does not decrypt with the current master key.</exception>
    public static async Task<ExchangeKey> KeyAsync(
        IExchangeAccountRepository accounts,
        ISecretProtector protector,
        ExchangeAccount account,
        CancellationToken cancellationToken)
    {
        EncryptedSecret secret = await accounts.GetSecretAsync(account.Id, cancellationToken) ?? throw new ConflictException("The exchange account is gone.");

        try
        {
            return new ExchangeKey(account.ApiKey, protector.Decrypt(account.Id, secret));
        }
        catch (CryptographicException)
        {
            throw new ConflictException("The secret of the account does not decrypt with the current master key; delete the account and add it again.");
        }
    }
}