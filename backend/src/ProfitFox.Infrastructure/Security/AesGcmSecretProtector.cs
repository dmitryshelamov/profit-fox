using System.Security.Cryptography;
using System.Text;

using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Infrastructure.Security;

/// <summary>
///     AES-256-GCM with a fresh 12-byte nonce and a 16-byte tag per value. The associated data
///     <c>exchange-account:{id:D}:secret</c> binds a value to its account. The master key never reaches a log.
/// </summary>
internal sealed class AesGcmSecretProtector(ExchangeAccountSettings settings) : ISecretProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public EncryptedSecret Encrypt(Guid accountId, string secret)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        try
        {
            using var aes = new AesGcm(Convert.FromBase64String(settings.MasterKey!), TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(accountId));

            return new EncryptedSecret(nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Decrypt(Guid accountId, EncryptedSecret secret)
    {
        var plaintext = new byte[secret.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(Convert.FromBase64String(settings.MasterKey!), TagLength);
            aes.Decrypt(secret.Nonce, secret.Ciphertext, secret.Tag, plaintext, AssociatedData(accountId));

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(Guid accountId)
    {
        return Encoding.UTF8.GetBytes($"exchange-account:{accountId:D}:secret");
    }
}