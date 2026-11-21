namespace ProfitFox.Presentation.Signals;

/// <summary>
///     A signal in the list (spec signal-constructor «Список сигналов»): its name, description and latest version, or why it
///     does not read.
/// </summary>
public sealed record SignalListItem(string Name, string? Description, int Version, string? Error);
