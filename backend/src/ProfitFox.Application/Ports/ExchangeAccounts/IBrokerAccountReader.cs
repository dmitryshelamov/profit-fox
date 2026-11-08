namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>Asks the broker about the accounts a token opens and what an account holds. Never sends orders.</summary>
public interface IBrokerAccountReader
{
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The broker does not accept the token.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The broker did not answer.</exception>
    Task<IReadOnlyList<BrokerAccount>> ListAccountsAsync(string token, CancellationToken cancellationToken);

    /// <summary>The portfolio of the account in its currency: the money and every position with a non-zero quantity, the most valuable first.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The broker does not accept the token or the token has no access to the account.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The broker did not answer.</exception>
    Task<AccountBalance> GetPortfolioAsync(string token, string accountId, CancellationToken cancellationToken);
}