using System.Text;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Application.Commands.Accounts;

/// <summary>
///     A protector that is no cipher: it keeps the secret backwards with the id of its account as the nonce, so a test sees
///     what was protected for whom. A master key that changed is <see cref="Changed" />: then nothing reads.
/// </summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public bool Changed { get; set; }

    public ProtectedSecret Protect(Guid accountId, string secret)
    {
        return new ProtectedSecret(accountId.ToByteArray(), [.. Encoding.UTF8.GetBytes(secret).Reverse()], [1]);
    }

    public string Reveal(Guid accountId, ProtectedSecret secret)
    {
        if (Changed || !secret.Nonce.SequenceEqual(accountId.ToByteArray()))
        {
            throw new SecretUnreadableException(accountId);
        }

        return Encoding.UTF8.GetString([.. secret.Ciphertext.Reverse()]);
    }
}
