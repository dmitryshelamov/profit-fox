namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     A signal in the list (spec signal-constructor «Список сигналов»): its name, description and latest version, or why it
///     does not read, and its id (spec signal-constructor «Идентификатор сигнала»), known for a signal that reads.
/// </summary>
public sealed record SignalListItem(string Name, string? Description, int Version, string? Error, Guid? Id);
