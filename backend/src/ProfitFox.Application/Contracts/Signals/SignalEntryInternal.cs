namespace ProfitFox.Application.Contracts.Signals;

/// <summary>
///     A signal in the list of the library: its name, description and latest version, or why it does not read.
/// </summary>
public sealed record SignalEntryInternal(string Name, string? Description, int Version, string? Error);
