using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     What a reference of a <c>signal</c> node found: the checked signal of the version, or why there is none. The name
///     and the latest version are known whenever the signal itself is saved, also when its version is not.
/// </summary>
public sealed record SignalLookup(SignalReference Reference)
{
    public string? Name { get; init; }

    public int? LatestVersion { get; init; }

    public Signal? Signal { get; init; }

    /// <summary>
    ///     <see cref="GraphErrorCode.UnknownSignal" /> or <see cref="GraphErrorCode.UnreadableSignal" />; null when the
    ///     signal is found.
    /// </summary>
    public GraphErrorCode? Problem { get; init; }

    public string? Reason { get; init; }
}
