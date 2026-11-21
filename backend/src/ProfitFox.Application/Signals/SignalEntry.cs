namespace ProfitFox.Application.Signals;

/// <summary>
///     A signal in the list of the library: its name, description and latest version, or why it does not read.
/// </summary>
public sealed record SignalEntry(string Name, string? Description, int Version, string? Error);
