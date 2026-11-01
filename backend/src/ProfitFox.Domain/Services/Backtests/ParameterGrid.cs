using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     Every combination of parameter values, numbered from 0 in a mixed radix: parameters ordered by name (ordinal), values of a range
///     ascending, the last parameter changing fastest. The numbers stay the same however the combinations are run.
/// </summary>
public sealed class ParameterGrid
{
    private readonly KeyValuePair<string, ParameterSpec>[] _parameters;

    /// <param name="parameters">A valid spec for every parameter of the strategy.</param>
    public ParameterGrid(IReadOnlyDictionary<string, ParameterSpec> parameters)
    {
        _parameters = [.. parameters.OrderBy(parameter => parameter.Key, StringComparer.Ordinal)];
        Count = CountOf(_parameters);
    }

    /// <summary>The number of combinations; <c>null</c> when it does not fit a <see cref="long" />.</summary>
    public long? Count { get; }

    public IReadOnlyDictionary<string, NodeValue> Combination(long index)
    {
        var values = new Dictionary<string, NodeValue>();

        for (int position = _parameters.Length - 1; position >= 0; position--)
        {
            (string name, ParameterSpec spec) = _parameters[position];
            values[name] = spec.ValueAt(index % spec.Count);
            index /= spec.Count;
        }

        return values;
    }

    private static long? CountOf(KeyValuePair<string, ParameterSpec>[] parameters)
    {
        try
        {
            return parameters.Aggregate(1L, (count, parameter) => checked(count * parameter.Value.Count));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}