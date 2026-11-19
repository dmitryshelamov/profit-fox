namespace ProfitFox.Bench.Presentation.Signals;

/// <summary>
///     A signal in the list (spec signal-constructor «Список сигналов»): its name and description, or why its file does
///     not read.
/// </summary>
public sealed record SignalListItem(string Name, string? Description, string? Error);
