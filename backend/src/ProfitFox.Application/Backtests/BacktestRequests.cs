using System.Text.Json;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Backtests;

/// <summary>Reading of a new backtest as the user sent it; errors are keyed like the API body, such as <c>parameters.fast</c>.</summary>
public static class BacktestRequests
{
    /// <exception cref="ValidationFailedException">A setting is malformed.</exception>
    public static (DateTimeOffset From, DateTimeOffset To, IReadOnlyDictionary<string, ParameterSpec> Parameters, SimulationRequest Simulation) Read(
        string from,
        string to,
        JsonElement? parameters,
        string? searchMode,
        decimal? balance,
        decimal? limitFeePercent,
        decimal? marketFeePercent,
        decimal? slippagePercent,
        string? limitFillMode)
    {
        var errors = new Dictionary<string, string>();

        if (!ApiTimestamps.TryParse(from, out DateTimeOffset start))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        if (!ApiTimestamps.TryParse(to, out DateTimeOffset end))
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        var specs = new Dictionary<string, ParameterSpec>();

        if (parameters is { ValueKind: not JsonValueKind.Object })
        {
            errors["parameters"] = "Parameters must be a JSON object.";
        }
        else if (parameters is { } given)
        {
            foreach (JsonProperty parameter in given.EnumerateObject())
            {
                if (BacktestJson.Read(parameter.Value) is { } spec)
                {
                    specs[parameter.Name] = spec;
                }
                else
                {
                    errors[$"parameters.{parameter.Name}"] = "Parameter must be a value or a range { \"min\": …, \"max\": …, \"step\": … }.";
                }
            }
        }

        if (searchMode is not null && !BacktestCodes.TryParseSearchMode(searchMode, out _))
        {
            errors["searchMode"] = "Search mode must be grid.";
        }

        LimitFillMode? mode = null;

        if (limitFillMode is not null)
        {
            if (BacktestCodes.TryParseMode(limitFillMode, out LimitFillMode parsed))
            {
                mode = parsed;
            }
            else
            {
                errors["simulation.limitFillMode"] = "Limit fill mode must be touch or breakout.";
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return (start.ToUniversalTime(), end.ToUniversalTime(), specs, new SimulationRequest(balance, limitFeePercent, marketFeePercent, slippagePercent, mode));
    }

    /// <exception cref="ValidationFailedException">The backtest is invalid against its bot and strategy.</exception>
    public static T Check<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (BacktestValidationException exception)
        {
            throw new ValidationFailedException(
                exception.Errors.GroupBy(Key).ToDictionary(group => group.Key, group => string.Join(" ", group.Select(error => error.Reason))));
        }
    }

    private static string Key(BacktestError error)
    {
        return error.Field switch
        {
            BacktestField.Period => "period",
            BacktestField.To => "to",
            BacktestField.Parameter => $"parameters.{error.Parameter}",
            BacktestField.Combinations => "parameters",
            BacktestField.Balance => "simulation.balance",
            BacktestField.LimitFee => "simulation.limitFeePercent",
            BacktestField.MarketFee => "simulation.marketFeePercent",
            _ => "simulation.slippagePercent"
        };
    }
}