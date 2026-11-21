namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A strategy in the list of the library: its name, description and latest version, or why it does not read.
/// </summary>
public sealed record StrategyEntryInternal(string Name, string? Description, int Version, string? Error);
