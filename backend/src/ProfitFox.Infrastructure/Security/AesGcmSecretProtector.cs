using System.Security.Cryptography;
using System.Text;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Infrastructure.Security;

/// <summary>
///     The secret of a key under AES-256-GCM by the master key (change add-exchange-accounts, design.md, decision 1): a fresh
///     random nonce of 12 bytes for each encryption, a tag of 16 bytes, and <c>exchange-account:{id}:secret</c> as the
///     associated data, so a secret written into the row of another account does not decrypt. The bytes of the plain text
///     are wiped once they are no longer needed.
/// </summary>
internal sealed class AesGcmSecretProtector(byte[] masterKey) : ISecretProtector
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key = masterKey.Length == MasterKey.Bytes
        ? [.. masterKey]
        : throw new ArgumentException($"The master key is {MasterKey.Bytes} bytes.", nameof(masterKey));

    public ProtectedSecret Protect(Guid accountId, string secret)
    {
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var ciphertext = new byte[plain.Length];
            var tag = new byte[TagBytes];
            using var aes = new AesGcm(_key, TagBytes);
            aes.Encrypt(nonce, plain, ciphertext, tag, Associated(accountId));
            return new ProtectedSecret(nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string Reveal(Guid accountId, ProtectedSecret secret)
    {
        if (secret.Nonce.Length != NonceBytes || secret.Tag.Length != TagBytes)
        {
            throw new SecretUnreadableException(accountId);
        }

        var plain = new byte[secret.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(secret.Nonce, secret.Ciphertext, secret.Tag, plain, Associated(accountId));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException exception)
        {
            throw new SecretUnreadableException(accountId, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Associated(Guid accountId)
    {
        return Encoding.UTF8.GetBytes($"exchange-account:{accountId}:secret");
    }
}
