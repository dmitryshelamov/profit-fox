using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Application.Ports.Security;

/// <summary>
///     Encrypts the secret of an API key by the master key of the server and reads it back (change add-exchange-accounts,
///     design.md, decision 1). A secret is bound to its account: encrypted for one, it does not read for another.
/// </summary>
public interface ISecretProtector
{
    ProtectedSecret Protect(Guid accountId, string secret);

    /// <summary>
    ///     The secret of the account, for one call of the exchange; the caller lets it go right after.
    /// </summary>
    /// <exception cref="SecretUnreadableException">
    ///     The secret was encrypted by another master key, for another account, or was changed.
    /// </exception>
    string Reveal(Guid accountId, ProtectedSecret secret);
}
