using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     The versions of saved signals a strategy may point at (change add-strategy-constructor, design.md, decision 2): the
///     domain reads no store, so the application reads the versions a graph refers to and gives them here. Each version is
///     checked as a signal once, when the set is made.
/// </summary>
public sealed class SignalSet
{
    private readonly Dictionary<Guid, (string Name, int Latest)> _signals = [];
    private readonly Dictionary<SignalReference, SignalLookup> _versions = [];

    /// <summary>
    ///     No saved signals: every <c>signal</c> node is an unknown signal.
    /// </summary>
    public static SignalSet Empty { get; } = new SignalSet([]);

    public SignalSet(IEnumerable<SignalVersion> versions)
    {
        foreach (SignalVersion version in versions)
        {
            _signals[version.Reference.Signal] = (version.Name, version.LatestVersion);
            if (version.Reference.Version < 1 || version.Reference.Version > version.LatestVersion)
            {
                continue;
            }

            var found = new SignalLookup(version.Reference) { Name = version.Name, LatestVersion = version.LatestVersion };
            List<GraphError> errors = version.Graph is null ? [] : GraphValidator.ValidateSignal(version.Graph);
            _versions[version.Reference] = version.Graph is null || errors.Count > 0
                ? found with
                {
                    Problem = GraphErrorCode.UnreadableSignal,
                    Reason = $"Version {version.Reference.Version} of the signal {version.Name} does not read as a signal."
                }
                : found with { Signal = new Signal(version.Graph) };
        }
    }

    public SignalLookup Find(SignalReference reference)
    {
        if (_versions.TryGetValue(reference, out SignalLookup? found))
        {
            return found;
        }

        if (_signals.TryGetValue(reference.Signal, out (string Name, int Latest) signal))
        {
            return new SignalLookup(reference)
            {
                Name = signal.Name,
                LatestVersion = signal.Latest,
                Problem = GraphErrorCode.UnknownSignal,
                Reason = $"The signal {signal.Name} has no version {reference.Version}; its latest is {signal.Latest}."
            };
        }

        return new SignalLookup(reference) { Problem = GraphErrorCode.UnknownSignal, Reason = $"There is no saved signal {reference.Signal}." };
    }
}
