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
    ///     Keeps the margin mode Bybit named now in place of the one before; false when there is no account.
    /// </summary>
    Task<bool> SetMarginModeAsync(Guid id, string? marginMode, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes how the trading Worker reads the account — <c>stream</c>, <c>poll</c> or null — and since when, when it changed,
    ///     under the lease of <paramref name="worker" /> as every write of the trading Worker (change take-idea-trades).
    /// </summary>
    /// <exception cref="Exceptions.LeaseLostException">The Worker does not hold the lease; nothing is written.</exception>
    Task SetFeedAsync(Guid id, string? feed, DateTimeOffset at, string worker, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes from which moment the fills of the account are read by REST, under the lease of <paramref name="worker" />.
    /// </summary>
    /// <exception cref="Exceptions.LeaseLostException">The Worker does not hold the lease; nothing is written.</exception>
    Task SetFillsCursorAsync(Guid id, DateTimeOffset at, string worker, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the account with its secret; false when there was none.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the account with its secret unless a trade of it holds its pair (change take-idea-trades; review R3): one
    ///     transaction under the advisory lock of the entry, so an entry and the removal do not cross. True when removed,
    ///     false when there was none, null when a trade holds a pair of it — nothing is removed then.
    /// </summary>
    Task<bool?> DeleteIdleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     The encrypted secret of the account; null when there is no account.
    /// </summary>
    Task<ProtectedSecret?> SecretAsync(Guid id, CancellationToken cancellationToken);
}
