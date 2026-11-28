using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Application.Commands.Accounts.AddAccount;

/// <summary>
///     The order of design.md, decision 5: the rules of the fields, without the network; the permissions of the key at Bybit;
///     the rules of the key; the encryption of the secret; the write. A taken name or UID is found by the unique index at the
///     write, so two additions of one account at once do not both pass.
/// </summary>
internal sealed class AddAccountCommandHandler(
    IExchangeAccountRepository accounts,
    IExchangeKeyInspector exchange,
    ISecretProtector protector,
    TimeProvider time) : IRequestHandler<AddAccountCommand, ExchangeAccount>
{
    /// <exception cref="AccountRequestException">A field or the key is refused, or the name or the account is taken.</exception>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key or the secret.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused for another reason.</exception>
    public async Task<ExchangeAccount> Handle(AddAccountCommand request, CancellationToken cancellationToken)
    {
        List<AccountProblem> problems = AccountRules.Check(request.Name, request.ApiKey, request.Secret);
        if (problems.Count > 0)
        {
            throw new AccountRequestException(problems);
        }

        string name = request.Name!.Trim();
        string apiKey = request.ApiKey!.Trim();
        string secret = request.Secret!.Trim();

        ExchangeKeyInfo key = await exchange.InspectAsync(apiKey, secret, cancellationToken);
        if (ExchangeKeyRules.Check(key) is { } refused)
        {
            throw new AccountRequestException([refused]);
        }

        // The database keeps a moment to the microsecond: the account is answered with the moment it is read back with.
        DateTimeOffset now = time.GetUtcNow();
        var account = new ExchangeAccount(Guid.NewGuid(), name, key.Uid, apiKey, now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)));
        ProtectedSecret protectedSecret = protector.Protect(account.Id, secret);
        if (await accounts.AddAsync(account, protectedSecret, cancellationToken))
        {
            return account;
        }

        IReadOnlyList<ExchangeAccount> all = await accounts.AllAsync(cancellationToken);
        if (all.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AccountRequestException([AccountRules.TakenName(name)]);
        }

        ExchangeAccount? same = all.FirstOrDefault(other => other.Uid == key.Uid);
        throw new AccountRequestException([same is null ? AccountRules.TakenName(name) : AccountRules.TakenAccount(same.Name)]);
    }
}
