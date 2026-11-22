using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A strategy in the list of the library: its name, description and latest version, or why it does not read. A strategy
///     that reads has the signals of its latest version, each version once, in the order of name and version, and the kinds
///     of the exit nodes of that version in the order stop, target, term (spec strategy-constructor «Сигналы и выходы
///     стратегии в списке»).
/// </summary>
public sealed record StrategyEntryInternal(string Name, string? Description, int Version, string? Error)
{
    /// <summary>
    ///     The kind of the strategy; null for one that does not read.
    /// </summary>
    public StrategyKind? Kind { get; init; }

    /// <summary>
    ///     The last backtest of the strategy (spec strategy-constructor «Последний прогон стратегии»); null without one.
    /// </summary>
    public LastBacktestInternal? LastRun { get; init; }

    public IReadOnlyList<StrategySignalInternal> Signals { get; init; } = [];

    public IReadOnlyList<string> Exits { get; init; } = [];
}
