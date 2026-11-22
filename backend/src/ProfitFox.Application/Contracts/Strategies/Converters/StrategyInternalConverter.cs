using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Application.Contracts.Strategies.Converters;

public static class StrategyInternalConverter
{
    public static StrategyInternal ToInternal(this StrategyDocument strategy, IReadOnlyList<SignalUseInternal> signals)
    {
        return new StrategyInternal(strategy.Name, strategy.Description, strategy.Graph, strategy.Layout, strategy.Version, signals, strategy.Raised);
    }

    /// <summary>
    ///     A strategy that reads, as a line of the list.
    /// </summary>
    public static StrategyEntryInternal ToEntryInternal(this StrategyDocument strategy)
    {
        return new StrategyEntryInternal(strategy.Name, strategy.Description, strategy.Version, Error: null);
    }
}
