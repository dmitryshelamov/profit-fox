using System.Text.Json;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots.Enums;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Bots;

/// <summary>Checks shared by the commands that create and change a bot; errors are keyed like the API body, such as <c>parameterValues.fast</c>.</summary>
public static class BotRequests
{
    /// <summary>Reads the settings of a bot as the user sent them, reporting every problem at once.</summary>
    /// <exception cref="ValidationFailedException">A setting is malformed.</exception>
    public static BotSettings Read(string name, JsonElement? parameterValues, string category, string symbol, string interval, decimal balance)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = "Name must not be empty.";
        }

        if (!MarketDataCodes.TryParseCategory(category, out MarketCategory parsedCategory))
        {
            errors["category"] = "Category must be spot or linear.";
        }

        if (!MarketDataCodes.TryParseInterval(interval, out Interval parsedInterval))
        {
            errors["interval"] = "Interval must be one of Bybit intervals: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M.";
        }

        var values = new Dictionary<string, NodeValue>();

        if (parameterValues is { ValueKind: not JsonValueKind.Object })
        {
            errors["parameterValues"] = "Parameter values must be a JSON object.";
        }
        else if (parameterValues is { } given)
        {
            foreach (JsonProperty value in given.EnumerateObject())
            {
                if (StrategyJson.ParseValue(value.Value) is { } parsed)
                {
                    values[value.Name] = parsed;
                }
                else
                {
                    errors[$"parameterValues.{value.Name}"] = "Value must be a number or a duration such as 01:00:00.";
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return new BotSettings(name, values, new Instrument(parsedCategory, symbol), parsedInterval, balance);
    }

    /// <summary>Runs a library operation on a bot and reports its errors by field.</summary>
    /// <exception cref="ValidationFailedException">The bot is invalid against its strategy.</exception>
    public static T Check<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (BotValidationException exception)
        {
            throw new ValidationFailedException(Errors(exception.Errors));
        }
    }

    private static async Task<T> Check<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (BotValidationException exception)
        {
            throw new ValidationFailedException(Errors(exception.Errors));
        }
    }

    /// <exception cref="ValidationFailedException">The exchange has no such symbol in the category.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    public static async Task EnsureListedAsync(IMarketHistoryClient marketHistory, Instrument instrument, CancellationToken cancellationToken)
    {
        if (!await marketHistory.InstrumentExistsAsync(instrument, cancellationToken))
        {
            throw new ValidationFailedException(
                new Dictionary<string, string> { ["symbol"] = $"Bybit has no {instrument.Symbol} in {instrument.Category.ToCode()}." });
        }
    }

    /// <summary>The account a bot is to trade with, <c>null</c> for none.</summary>
    /// <exception cref="ValidationFailedException">There is no such account.</exception>
    public static async Task<BotAccount?> AccountAsync(IExchangeAccountRepository accounts, Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } accountId)
        {
            return null;
        }

        ExchangeAccount account = await accounts.GetAsync(accountId, cancellationToken)
                                  ?? throw new ValidationFailedException(new Dictionary<string, string> { ["exchangeAccountId"] = "No such exchange account." });

        return new BotAccount(account.Id, account.Categories);
    }

    /// <summary>Saves the library; a bot whose account was deleted meanwhile is an error of the account.</summary>
    /// <exception cref="ValidationFailedException">The account of a saved bot is gone.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being saved.</exception>
    public static async Task SaveAsync(IStrategyLibraryRepository repository, StrategyLibrary library, CancellationToken cancellationToken)
    {
        StrategyRequests.EnsureSaved(await Check(() => repository.SaveAsync(library.Change, library.Revision, cancellationToken)));
    }

    public static Dictionary<string, string> Errors(IReadOnlyList<BotError> errors)
    {
        return errors.GroupBy(Key).ToDictionary(group => group.Key, group => string.Join(" ", group.Select(error => error.Reason)));
    }

    private static string Key(BotError error)
    {
        return error.Field switch
        {
            BotField.Strategy => "strategyId",
            BotField.Parameter => $"parameterValues.{error.Parameter}",
            BotField.Category => "category",
            BotField.Account => "exchangeAccountId",
            _ => "balance"
        };
    }
}