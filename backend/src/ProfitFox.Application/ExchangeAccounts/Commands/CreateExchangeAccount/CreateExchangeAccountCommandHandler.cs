using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;

public sealed class CreateExchangeAccountCommandHandler(
    IExchangeAccountRepository repository,
    IExchangeKeyInspector inspector,
    IBrokerAccountReader broker,
    ISecretProtector protector,
    TimeProvider time) : IRequestHandler<CreateExchangeAccountCommand, Guid>
{
    /// <summary>
    ///     The exchange is asked last, once everything else is valid: Bybit about the key, T-Invest about the accounts of the token, so an
    ///     access taken away since the account was chosen is seen. The secret is stored only encrypted.
    /// </summary>
    /// <exception cref="ValidationFailedException">A field is invalid, the exchange does not accept the key or the account, or it is added already.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    public Task<Guid> Handle(CreateExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        return request.Exchange == Exchange.TInvest ? CreateTInvestAsync(request, cancellationToken) : CreateBybitAsync(request, cancellationToken);
    }

    private async Task<Guid> CreateBybitAsync(CreateExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        CheckBybit(request);
        ExchangeKeyInfo key = await InspectAsync(request.ApiKey!, request.ApiSecret!, cancellationToken);
        IReadOnlyList<string> problems = ExchangeKeyRules.Check(key);

        if (problems.Count > 0)
        {
            throw KeyError(string.Join(" ", problems));
        }

        var account = new ExchangeAccount(Guid.NewGuid(), request.Name, request.ApiKey!, key.Uid, key.TradableCategories, time.GetUtcNow());

        if (!await repository.AddAsync(account, protector.Encrypt(account.Id, request.ApiSecret!), cancellationToken))
        {
            throw KeyError($"Bybit account {key.Uid} is already added.");
        }

        return account.Id;
    }

    private async Task<Guid> CreateTInvestAsync(CreateExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        CheckTInvest(request);
        IReadOnlyList<BrokerAccount> accounts = await ListAccountsAsync(request.Token!, cancellationToken);
        BrokerAccount chosen = accounts.FirstOrDefault(account => account.Id == request.AccountId)
                               ?? throw AccountError($"The token opens no account {request.AccountId}.");

        if (BrokerAccountRules.Problem(chosen) is { } problem)
        {
            throw AccountError(BrokerAccountRules.Describe(problem));
        }

        var account = new ExchangeAccount(
            Guid.NewGuid(),
            request.Name,
            Exchange.TInvest,
            null,
            null,
            chosen.Id,
            new HashSet<MarketCategory> { MarketCategory.Moex },
            time.GetUtcNow());

        if (!await repository.AddAsync(account, protector.Encrypt(account.Id, request.Token!), cancellationToken))
        {
            throw AccountError($"T-Invest account {chosen.Id} is already added.");
        }

        return account.Id;
    }

    private static void CheckBybit(CreateExchangeAccountCommand request)
    {
        var errors = new Dictionary<string, string>();
        CheckName(request, errors);

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            errors["apiKey"] = "API key must not be empty.";
        }

        if (string.IsNullOrWhiteSpace(request.ApiSecret))
        {
            errors["apiSecret"] = "API secret must not be empty.";
        }

        Throw(errors);
    }

    private static void CheckTInvest(CreateExchangeAccountCommand request)
    {
        var errors = new Dictionary<string, string>();
        CheckName(request, errors);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors["token"] = "Token must not be empty.";
        }

        if (string.IsNullOrWhiteSpace(request.AccountId))
        {
            errors["accountId"] = "Account must not be empty.";
        }

        Throw(errors);
    }

    private static void CheckName(CreateExchangeAccountCommand request, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = "Name must not be empty.";
        }
    }

    private static void Throw(Dictionary<string, string> errors)
    {
        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }
    }

    private async Task<ExchangeKeyInfo> InspectAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            return await inspector.InspectAsync(apiKey, apiSecret, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw KeyError(exception.Message);
        }
    }

    private async Task<IReadOnlyList<BrokerAccount>> ListAccountsAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            return await broker.ListAccountsAsync(token, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["token"] = exception.Message });
        }
    }

    private static ValidationFailedException KeyError(string reason)
    {
        return new ValidationFailedException(new Dictionary<string, string> { ["apiKey"] = reason });
    }

    private static ValidationFailedException AccountError(string reason)
    {
        return new ValidationFailedException(new Dictionary<string, string> { ["accountId"] = reason });
    }
}