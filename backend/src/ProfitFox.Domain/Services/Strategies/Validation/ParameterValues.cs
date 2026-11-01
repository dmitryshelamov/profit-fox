using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Validation;

/// <summary>Values a bot or a backtest gives to strategy parameters.</summary>
public static class ParameterValues
{
    /// <summary>Every value that breaks its parameter's limits, in the order of the parameters, then every value of an unknown parameter.</summary>
    public static IReadOnlyList<(string Parameter, string Reason)> Check(IReadOnlyList<StrategyParameter> parameters, IReadOnlyDictionary<string, NodeValue> values)
    {
        var errors = new List<(string Parameter, string Reason)>();

        foreach (StrategyParameter parameter in parameters)
        {
            string? reason = values.TryGetValue(parameter.Name, out NodeValue? value) ? parameter.Reject(value) : null;

            if (reason is not null)
            {
                errors.Add((parameter.Name, $"Value {reason}"));
            }
        }

        foreach (string unknown in values.Keys.Where(name => parameters.All(parameter => parameter.Name != name)))
        {
            errors.Add((unknown, "No such parameter."));
        }

        return errors;
    }
}