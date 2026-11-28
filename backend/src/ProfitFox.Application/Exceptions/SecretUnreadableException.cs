namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The secret of an account does not decrypt (design.md, decision 6): another master key encrypted it, it was written to
///     another account or changed. The account has to be added again; the API answers 409.
/// </summary>
public sealed class SecretUnreadableException(Guid accountId, Exception? inner = null)
    : Exception($"The secret of the account {accountId} does not decrypt: add the account again.", inner);
