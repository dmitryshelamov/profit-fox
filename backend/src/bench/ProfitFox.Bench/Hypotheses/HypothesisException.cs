namespace ProfitFox.Bench.Hypotheses;

/// <summary>
///     A hypothesis file that cannot be run, with every problem found in it.
/// </summary>
public sealed class HypothesisException(string folder, IReadOnlyList<string> errors)
    : Exception($"{folder}: {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
