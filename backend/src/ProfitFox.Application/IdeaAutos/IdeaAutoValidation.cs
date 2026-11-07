using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>The checks of an auto on a save (requirement "Проверка автомата"): the first error of each field goes by the name of the field.</summary>
public static class IdeaAutoValidation
{
    public const int MaxNameLength = 60;

    /// <summary>The conditions of the input, or the errors by field.</summary>
    /// <exception cref="ValidationFailedException">A field is wrong.</exception>
    public static async Task<IdeaAutoConditions> ParseAsync(
        IdeaAutoInput input,
        Guid? own,
        IIdeaAutoRepository autos,
        IExchangeAccountRepository accounts,
        IIdeaTradingSettingsRepository settings,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        string name = input.Name.Trim();

        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            errors["name"] = $"The name must be from 1 to {MaxNameLength} characters.";
        }
        else if ((await autos.GetAllAsync(cancellationToken)).Any(auto => auto.Id != own && auto.Name == name))
        {
            errors["name"] = "An auto with this name already exists.";
        }

        if (await accounts.GetAsync(input.AccountId, cancellationToken) is null)
        {
            errors["accountId"] = "There is no such exchange account.";
        }

        if (input.Amount <= 0m)
        {
            errors["amount"] = "The amount must be above zero.";
        }
        else if (input.Amount > (await settings.GetAsync(cancellationToken)).MaxAmount)
        {
            errors["amount"] = "The amount must not be above the largest amount of a trade.";
        }

        if (input.Budget < input.Amount)
        {
            errors["budget"] = "The budget must not be below the amount of a trade.";
        }

        IReadOnlyList<IdeaSetup> setups = Codes<IdeaSetup>(input.Setups, "setups", IdeaCodes.TryParseSetup, "There must be a setup, and each must be a known one.", errors);
        IReadOnlyList<IdeaType> types = Codes<IdeaType>(input.Types, "types", IdeaCodes.TryParseType, "There must be a type, and each must be a known one.", errors);
        IReadOnlyList<Interval> intervals = Codes<Interval>(input.Intervals, "intervals", TryParseInterval, "There must be an interval of 5 minutes, 15 minutes, 1 hour or 4 hours.", errors);

        if (types.Contains(IdeaType.StopEntry))
        {
            errors["types"] = "An auto never takes a stop entry.";
        }

        IdeaSide? side = Optional<IdeaSide>(input.Side, "side", IdeaCodes.TryParseSide, errors);
        MarketCategory? category = Optional<MarketCategory>(input.Category, "category", MarketDataCodes.TryParseCategory, errors);
        IdeaBadge? badge = Optional<IdeaBadge>(input.Badge, "badge", IdeaCodes.TryParseBadge, errors);

        if (input.MinRiskReward < 0m)
        {
            errors["minRiskReward"] = "The lowest reward to the risk must not be negative.";
        }

        if (input.MinCases < 0)
        {
            errors["minCases"] = "The least number of cases must not be negative.";
        }

        FeatureCondition dayDrop = Feature(input.DayDrop, "dayDrop", errors);
        FeatureCondition btcDrop = Feature(input.BtcDrop, "btcDrop", errors);
        FeatureCondition highFunding = Feature(input.HighFunding, "highFunding", errors);

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return new IdeaAutoConditions(setups, types, intervals, side, category, badge, input.MinRiskReward, input.MinCases, dayDrop, btcDrop, highFunding);
    }

    private static List<T> Codes<T>(IReadOnlyList<string> codes, string field, TryParse<T> parse, string message, Dictionary<string, string> errors)
    {
        var parsed = new List<T>();

        foreach (string code in codes)
        {
            if (!parse(code, out T value))
            {
                errors[field] = message;

                return parsed;
            }

            if (!parsed.Contains(value))
            {
                parsed.Add(value);
            }
        }

        if (parsed.Count == 0)
        {
            errors[field] = message;
        }

        return parsed;
    }

    private static T? Optional<T>(string? code, string field, TryParse<T> parse, Dictionary<string, string> errors)
        where T : struct
    {
        if (code is null)
        {
            return null;
        }

        if (parse(code, out T value))
        {
            return value;
        }

        errors[field] = $"Unknown {field}.";

        return null;
    }

    // A condition by a feature left out is «любой» (decision 8 of add-market-idea-features).
    private static FeatureCondition Feature(string? code, string field, Dictionary<string, string> errors)
    {
        return Optional<FeatureCondition>(code, field, IdeaAutoCodes.TryParseFeatureCondition, errors) ?? FeatureCondition.Any;
    }

    // The ideas come on 5 minutes, 15 minutes, 1 hour and 4 hours only.
    private static bool TryParseInterval(string code, out Interval interval)
    {
        return MarketDataCodes.TryParseInterval(code, out interval) && IdeasSettings.Intervals.Contains(interval);
    }

    private delegate bool TryParse<T>(string code, out T value);
}