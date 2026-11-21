namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A version of a saved signal as the library gives it to the checks of a strategy: the signal's current name and latest
///     version, and the graph of this version, or null when its text does not read. A version above the latest is not a
///     version.
/// </summary>
public sealed record SignalVersion(SignalReference Reference, string Name, int LatestVersion, GraphDefinition? Graph);
