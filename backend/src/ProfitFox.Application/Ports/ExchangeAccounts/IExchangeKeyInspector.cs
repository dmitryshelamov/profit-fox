namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>Asks the exchange about an account by its API key: what the key may do and what the account holds. Never sends orders.</summary>
public interface IExchangeKeyInspector
{
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The exchange does not accept the key.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken);

    /// <summary>Coins with a non-zero balance, the most valuable first.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The exchange does not accept the key.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<AccountBalance> GetBalanceAsync(string apiKey, string secret, CancellationToken cancellationToken);
}