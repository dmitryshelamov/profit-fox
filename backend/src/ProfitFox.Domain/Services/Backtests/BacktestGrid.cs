using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The sets of a backtest (spec strategy-backtest «Наборы значений»; change add-strategy-backtest, design.md, decision
///     3): every combination of one value of each parameter that was given values and one timeframe — the parameters in the
///     order of the graph, the first changing slowest, then the timeframe. The number of a set is its place in this order,
///     from 1. The frontend counts the sets of its form by the same rule.
/// </summary>
public static class BacktestGrid
{
    /// <summary>
    ///     How many sets the values and the timeframes give, without making them: a parameter without values counts once.
    /// </summary>
    public static long Count(IEnumerable<ParameterValues> values, int timeframes)
    {
        long count = timeframes;
        foreach (ParameterValues parameter in values.Where(parameter => parameter.Values.Count > 0))
        {
            count = Math.Min(count * parameter.Values.Count, (long)Backtest.MaxSets + 1);
        }

        return count;
    }

    /// <summary>
    ///     The sets of checked settings in the order of the grid.
    /// </summary>
    public static IReadOnlyList<BacktestSet> Of(BacktestSettings settings)
    {
        return Of(settings.Values, settings.Timeframes);
    }

    /// <summary>
    ///     The sets of <paramref name="values" />, given in the order of the parameters of the graph, and of
    ///     <paramref name="timeframes" />.
    /// </summary>
    public static IReadOnlyList<BacktestSet> Of(IReadOnlyList<ParameterValues> values, IReadOnlyList<TimeSpan> timeframes)
    {
        ParameterValues[] given = [.. values.Where(parameter => parameter.Values.Count > 0)];
        List<List<KeyValuePair<string, string>>> combinations = [[]];
        foreach (ParameterValues parameter in given)
        {
            combinations = [.. combinations.SelectMany(combination => parameter.Values.Select(value => (List<KeyValuePair<string, string>>)[.. combination, new(parameter.Name, value)]))];
        }

        List<BacktestSet> sets = [];
        foreach (List<KeyValuePair<string, string>> combination in combinations)
        {
            foreach (TimeSpan timeframe in timeframes)
            {
                sets.Add(new BacktestSet(sets.Count + 1, timeframe, combination));
            }
        }

        return sets;
    }
}
