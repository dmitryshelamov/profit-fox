using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;

public sealed class SaveIdeaTradingSettingsCommandHandler(IIdeaTradingSettingsRepository repository, IExchangeAccountRepository accounts) : IRequestHandler<SaveIdeaTradingSettingsCommand>
{
    /// <exception cref="ValidationFailedException">An unknown account, an amount, a largest amount or a total risk not above zero, fewer than one trade, a negative reward to the risk, a daily stop not above zero, fewer than one trade of a side, or a budget of all the autos not above zero.</exception>
    public async Task Handle(SaveIdeaTradingSettingsCommand request, CancellationToken cancellationToken)
    {
        IdeaTradingSettings settings = request.Settings;
        var errors = new Dictionary<string, string>();

        if (settings.AccountId is { } account && await accounts.GetAsync(account, cancellationToken) is null)
        {
            errors["accountId"] = "There is no such exchange account.";
        }

        if (settings.Amount <= 0m)
        {
            errors["amount"] = "The amount of a trade must be above zero.";
        }

        if (settings.MaxAmount <= 0m)
        {
            errors["maxAmount"] = "The largest amount of a trade must be above zero.";
        }

        if (settings.MaxOpenTrades < 1)
        {
            errors["maxOpenTrades"] = "At least one open trade must be allowed.";
        }

        if (settings.MaxTotalRisk <= 0m)
        {
            errors["maxTotalRisk"] = "The total risk must be above zero.";
        }

        if (settings.MinRiskReward < 0m)
        {
            errors["minRiskReward"] = "The lowest reward to the risk must not be negative.";
        }

        if (settings.DailyLossLimit <= 0m)
        {
            errors["dailyLossLimit"] = "The daily stop must be above zero.";
        }

        if (settings.MaxSameDirection < 1)
        {
            errors["maxSameDirection"] = "At least one trade of a side must be allowed.";
        }

        if (settings.AutosBudget <= 0m)
        {
            errors["autosBudget"] = "The budget of all the autos must be above zero.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        await repository.SaveAsync(settings, cancellationToken);
    }
}