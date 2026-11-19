namespace ProfitFox.Bench.Application.Signals;

/// <summary>
///     A signal in the list of the library: its name and description, or why its file does not read.
/// </summary>
public sealed record SignalEntry(string Name, string? Description, string? Error);
