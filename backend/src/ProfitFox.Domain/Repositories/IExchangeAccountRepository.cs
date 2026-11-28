using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the accounts of the exchange are kept (change add-exchange-accounts, design.md, decision 3): a name is one of its
///     kind without regard to case, and so is a UID. The encrypted secret is given only by <see cref="SecretAsync" />, to the
///     handlers that call the exchange.
/// </summary>
public interface IExchangeAccountRepository
{
    /// <summary>
    ///     Every account, in the order they were added.
    /// </summary>
    Task<IReadOnlyList<ExchangeAccount>> AllAsync(CancellationToken cancellationToken);

    Task<ExchangeAccount?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes a new account with its encrypted secret; false when another account has the name or the UID.
    /// </summary>
    Task<bool> AddAsync(ExchangeAccount account, ProtectedSecret secret, CancellationToken cancellationToken);

    /// <summary>
    ///     Gives the account a new name; false when another account has it. The key, the secret and the UID stay.
    /// </summary>
    Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the account with its secret; false when there was none.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     The encrypted secret of the account; null when there is no account.
    /// </summary>
    Task<ProtectedSecret?> SecretAsync(Guid id, CancellationToken cancellationToken);
}
