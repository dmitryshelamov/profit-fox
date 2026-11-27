using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The sets of a backtest as its pairs compute them (change add-strategy-backtest, design.md, decisions 3 and 4): for
///     each set the expanded graph of the version with the values of the set, the strategy it is, and its design — the
///     history and the costs of the settings, the timeframe of the set and the seed of its graph and timeframe.
/// </summary>
public static class BacktestPlans
{
    /// <exception cref="InvalidOperationException">A set does not read as a strategy of its kind: the settings were not checked.</exception>
    public static IReadOnlyList<BacktestPlan> Of(GraphDefinition graph, StrategyKind kind, BacktestSettings settings, IReadOnlyList<BacktestSet> sets)
    {
        List<BacktestPlan> plans = [];
        foreach (BacktestSet set in sets)
        {
            List<GraphError> errors = [];
            GraphDefinition changed = GraphParameters.With(graph, set.Values, errors);
            ReadResult<TradingStrategy> read = errors.Count > 0 ? ReadResult<TradingStrategy>.Invalid(errors) : StrategyGraph.ReadStrategy(changed, kind);
            if (read.Value is not { } strategy)
            {
                throw new InvalidOperationException($"Set {set.Number} does not read as a strategy: {string.Join("; ", read.Errors)}");
            }

            var design = new BacktestDesign(settings.Start, settings.End, set.Timeframe, BacktestSeed.Of(changed, set.Timeframe), settings.Costs);
            plans.Add(new BacktestPlan(set.Number, strategy, design));
        }

        return plans;
    }
}
