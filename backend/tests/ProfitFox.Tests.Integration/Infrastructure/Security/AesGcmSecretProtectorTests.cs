using System.Security.Cryptography;
using System.Text;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Infrastructure.Security;

namespace ProfitFox.Tests.Integration.Infrastructure.Security;

/// <summary>
///     Change add-exchange-accounts, task 2.1 (design.md, decision 1): the secret under AES-256-GCM by the master key, bound
///     to its account; spec exchange-accounts «Подменённая строка».
/// </summary>
public sealed class AesGcmSecretProtectorTests
{
    private const string Secret = "s3cr3t-value";
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Test]
    public void A_secret_encrypted_for_an_account_reads_back_for_it()
    {
        var protector = new AesGcmSecretProtector(Key(1));

        ProtectedSecret secret = protector.Protect(Main, Secret);

        Assert.That(protector.Reveal(Main, secret), Is.EqualTo(Secret));
        Assert.That(secret.Nonce, Has.Length.EqualTo(12));
        Assert.That(secret.Tag, Has.Length.EqualTo(16));
        Assert.That(Encoding.UTF8.GetString(secret.Ciphertext), Does.Not.Contain(Secret));
    }

    [Test]
    public void Two_encryptions_of_one_secret_differ()
    {
        var protector = new AesGcmSecretProtector(Key(1));

        ProtectedSecret first = protector.Protect(Main, Secret);
        ProtectedSecret second = protector.Protect(Main, Secret);

        Assert.That(second.Nonce, Is.Not.EqualTo(first.Nonce));
        Assert.That(second.Ciphertext, Is.Not.EqualTo(first.Ciphertext));
    }

    // Spec exchange-accounts «Подменённая строка».
    [Test]
    public void A_secret_written_into_the_row_of_another_account_does_not_decrypt()
    {
        var protector = new AesGcmSecretProtector(Key(1));
        ProtectedSecret secret = protector.Protect(Main, Secret);

        SecretUnreadableException? refused = Assert.Throws<SecretUnreadableException>(() => protector.Reveal(Other, secret));

        Assert.That(refused!.Message, Does.Not.Contain(Secret));
        Assert.That(refused.InnerException, Is.InstanceOf<CryptographicException>());
    }

    [Test]
    public void Another_master_key_does_not_decrypt()
    {
        ProtectedSecret secret = new AesGcmSecretProtector(Key(1)).Protect(Main, Secret);

        Assert.Throws<SecretUnreadableException>(() => new AesGcmSecretProtector(Key(2)).Reveal(Main, secret));
    }

    [Test]
    public void A_changed_ciphertext_does_not_decrypt()
    {
        var protector = new AesGcmSecretProtector(Key(1));
        ProtectedSecret secret = protector.Protect(Main, Secret);
        byte[] changed = [.. secret.Ciphertext];
        changed[0] ^= 1;

        Assert.Throws<SecretUnreadableException>(() => protector.Reveal(Main, secret with { Ciphertext = changed }));
    }

    private static byte[] Key(byte seed)
    {
        return [.. Enumerable.Range(0, 32).Select(index => (byte)(index * 7 + seed))];
    }
}
