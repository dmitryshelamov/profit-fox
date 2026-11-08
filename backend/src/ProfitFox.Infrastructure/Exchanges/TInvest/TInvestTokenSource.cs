using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The token the reads of market data without an account go with: the oldest T-Invest account of storage, decrypted and kept for
///     five minutes, so a deleted account stops being used within that time. Without an account the data of T-Invest are unavailable
///     and no request goes; an account just added is seen at once. One for the process.
/// </summary>
internal sealed class TInvestTokenSource(IServiceScopeFactory scopes, ISecretProtector protector, TimeProvider time)
{
    public const string UnavailableMessage = "T-Invest data is unavailable: there is no T-Invest account.";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private (DateTimeOffset Expires, string Token)? _cached;

    /// <exception cref="ExchangeUnavailableException">There is no T-Invest account.</exception>
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        if (_cached is { } fresh && now < fresh.Expires)
        {
            return fresh.Token;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_cached is { } refreshed && now < refreshed.Expires)
            {
                return refreshed.Token;
            }

            string token = await ReadAsync(cancellationToken);
            _cached = (now + Lifetime, token);

            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        IExchangeAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IExchangeAccountRepository>();
        ExchangeAccount account = (await accounts.GetAllAsync(cancellationToken))
                                  .Where(account => account.Exchange == Exchange.TInvest)
                                  .OrderBy(account => account.CreatedAt)
                                  .ThenBy(account => account.Id)
                                  .FirstOrDefault()
                                  ?? throw new ExchangeUnavailableException(UnavailableMessage);
        EncryptedSecret secret = await accounts.GetSecretAsync(account.Id, cancellationToken) ?? throw new ExchangeUnavailableException(UnavailableMessage);

        return protector.Decrypt(account.Id, secret);
    }
}