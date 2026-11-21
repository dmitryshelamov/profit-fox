namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     A signal in the list (spec signal-constructor «Список сигналов»): its name, description and latest version, or why it
///     does not read, its id (spec signal-constructor «Идентификатор сигнала»), known for a signal that reads, and the names
///     of the strategies any version of which points at it (spec signal-constructor «Стратегии сигнала в списке»).
/// </summary>
public sealed record SignalListItem(string Name, string? Description, int Version, string? Error, Guid? Id, IReadOnlyList<string> Strategies);
