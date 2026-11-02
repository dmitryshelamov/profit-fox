using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Application.Ports.Security;

/// <summary>Encrypts the secret of an exchange account so that it decrypts only for that account.</summary>
public interface ISecretProtector
{
    EncryptedSecret Encrypt(Guid accountId, string secret);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The value belongs to another account or was changed.</exception>
    string Decrypt(Guid accountId, EncryptedSecret secret);
}