namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A strategy in the list of the library: its name, description and latest version, or why it does not read. A strategy
///     that reads has the signals of its latest version, each version once, in the order of name and version, and the kinds
///     of the exit nodes of that version in the order stop, target, term (spec strategy-constructor «Сигналы и выходы
///     стратегии в списке»).
/// </summary>
public sealed record StrategyEntryInternal(string Name, string? Description, int Version, string? Error)
{
    public IReadOnlyList<StrategySignalInternal> Signals { get; init; } = [];

    public IReadOnlyList<string> Exits { get; init; } = [];
}
