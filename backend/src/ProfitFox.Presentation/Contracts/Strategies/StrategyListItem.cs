namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A strategy in the list (spec strategy-constructor «Список стратегий»): its name, description and latest version, or
///     why it does not read, the signals of its latest version and the kinds of the exit nodes of that version —
///     <c>stop-loss</c>, <c>take-profit</c>, <c>close-after</c> (spec strategy-constructor «Сигналы и выходы стратегии в
///     списке»).
/// </summary>
public sealed record StrategyListItem(
    string Name,
    string? Description,
    int Version,
    string? Error,
    IReadOnlyList<StrategySignalItem> Signals,
    IReadOnlyList<string> Exits);
