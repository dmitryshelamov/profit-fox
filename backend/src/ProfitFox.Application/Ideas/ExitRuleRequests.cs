using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas;

/// <summary>An exit rule asked for by the API: the type and the interval by their codes, the values checked against the grid of the interval.</summary>
public static class ExitRuleRequests
{
    public const string InvalidLife = "The lifetime must be a number of candles of the grid of the interval.";
    public const string InvalidTarget = "The target must be from 1 to 5 R by 0.5 R, and only for a type with a target in R.";
    public const string InvalidWindow = "The entry window must be a number of candles of the grid of the interval, and only for a pullback or a stop entry.";

    /// <exception cref="ValidationFailedException">An unknown type or interval, or a value the rule cannot take, by the name of its parameter.</exception>
    public static IdeaExitRule Parse(string? type, string? interval, int life, decimal? target, int? window)
    {
        var errors = new Dictionary<string, string>();

        if (!IdeaCodes.TryParseType(type ?? "", out IdeaType parsedType))
        {
            errors["type"] = IdeaQueries.InvalidType;
        }

        if (!IdeaQueries.TryParseInterval(interval, out Interval parsedInterval))
        {
            errors["interval"] = IdeaQueries.InvalidInterval;
        }

        if (errors.Count == 0)
        {
            foreach (string invalid in IdeaExitRule.Invalid(parsedType, parsedInterval, life, target, window))
            {
                errors[invalid] = invalid switch
                {
                    "life" => InvalidLife,
                    "target" => InvalidTarget,
                    _ => InvalidWindow
                };
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return new IdeaExitRule(parsedType, parsedInterval, life, target, window, null);
    }
}