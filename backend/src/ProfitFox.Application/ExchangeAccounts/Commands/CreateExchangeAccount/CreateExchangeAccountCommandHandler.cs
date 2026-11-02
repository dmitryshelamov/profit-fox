using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;

public sealed class CreateExchangeAccountCommandHandler(
    IExchangeAccountRepository repository,
    IExchangeKeyInspector inspector,
    ISecretProtector protector,
    TimeProvider time) : IRequestHandler<CreateExchangeAccountCommand, Guid>
{
    /// <summary>The key is checked on the exchange last, once everything else is valid; the secret is stored only encrypted.</summary>
    /// <exception cref="ValidationFailedException">A field is invalid, or the exchange does not accept the key or it is added already.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    public async Task<Guid> Handle(CreateExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        ExchangeEnvironment environment = Read(request);
        ExchangeKeyInfo key = await InspectAsync(environment, request, cancellationToken);
        IReadOnlyList<string> problems = ExchangeKeyRules.Check(key);

        if (problems.Count > 0)
        {
            throw KeyError(string.Join(" ", problems));
        }

        var account = new ExchangeAccount(Guid.NewGuid(), request.Name, environment, request.ApiKey, key.Uid, key.TradableCategories, time.GetUtcNow());

        if (!await repository.AddAsync(account, protector.Encrypt(account.Id, request.ApiSecret), cancellationToken))
        {
            throw KeyError($"Bybit account {key.Uid} is already added in {environment.ToCode()}.");
        }

        return account.Id;
    }

    private static ExchangeEnvironment Read(CreateExchangeAccountCommand request)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = "Name must not be empty.";
        }

        if (!ExchangeAccountCodes.TryParseEnvironment(request.Environment, out ExchangeEnvironment environment))
        {
            errors["environment"] = "Environment must be mainnet or testnet.";
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            errors["apiKey"] = "API key must not be empty.";
        }

        if (string.IsNullOrWhiteSpace(request.ApiSecret))
        {
            errors["apiSecret"] = "API secret must not be empty.";
        }

        return errors.Count > 0 ? throw new ValidationFailedException(errors) : environment;
    }

    private async Task<ExchangeKeyInfo> InspectAsync(ExchangeEnvironment environment, CreateExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        try
        {
            return await inspector.InspectAsync(environment, request.ApiKey, request.ApiSecret, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw KeyError(exception.Message);
        }
    }

    private static ValidationFailedException KeyError(string reason)
    {
        return new ValidationFailedException(new Dictionary<string, string> { ["apiKey"] = reason });
    }
}