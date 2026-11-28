namespace ProfitFox.Domain.ValueObjects.Accounts;

/// <summary>
///     The secret of an API key encrypted by the master key of the server (change add-exchange-accounts, design.md, decision
///     1): the nonce of the encryption, the ciphertext and the tag that proves it whole. Only the protector that made it reads
///     it, and only for the account it was made for.
/// </summary>
public sealed record ProtectedSecret(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
