using System.Security.Cryptography;

using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Security;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Security;

public class AesGcmSecretProtectorTests
{
    private static readonly Guid AccountId = Guid.Parse("6f9a3c1e-2b4d-4e8f-9a1b-3c5d7e9f1a2b");

    private readonly AesGcmSecretProtector _protector = new AesGcmSecretProtector(new ExchangeAccountSettings { MasterKey = Convert.ToBase64String(new byte[32]) });

    [Test]
    public void Decrypting_gives_the_original_secret()
    {
        EncryptedSecret encrypted = _protector.Encrypt(AccountId, "s3cr3t-value");

        _protector.Decrypt(AccountId, encrypted).ShouldBe("s3cr3t-value");
    }

    [Test]
    public void The_same_secret_encrypts_differently_each_time()
    {
        EncryptedSecret first = _protector.Encrypt(AccountId, "s3cr3t-value");
        EncryptedSecret second = _protector.Encrypt(AccountId, "s3cr3t-value");

        second.Nonce.ShouldNotBe(first.Nonce);
        second.Ciphertext.ShouldNotBe(first.Ciphertext);
    }

    // Decision 3: the associated data binds a secret to its account.
    [Test]
    public void A_secret_of_another_account_is_not_decrypted()
    {
        EncryptedSecret encrypted = _protector.Encrypt(AccountId, "s3cr3t-value");

        Should.Throw<CryptographicException>(() => _protector.Decrypt(Guid.NewGuid(), encrypted));
    }

    [Test]
    public void A_tampered_tag_is_not_decrypted()
    {
        EncryptedSecret encrypted = _protector.Encrypt(AccountId, "s3cr3t-value");
        byte[] tag = encrypted.Tag.ToArray();
        tag[0] ^= 1;

        Should.Throw<CryptographicException>(() => _protector.Decrypt(AccountId, encrypted with { Tag = tag }));
    }
}