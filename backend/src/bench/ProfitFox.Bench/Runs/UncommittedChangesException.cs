namespace ProfitFox.Bench.Runs;

/// <summary>
///     The repository has uncommitted changes: a verdict would not describe a commit.
/// </summary>
public sealed class UncommittedChangesException(IReadOnlyList<string> paths)
    : Exception($"uncommitted changes: {string.Join(", ", paths.Take(10))}{(paths.Count > 10 ? ", …" : "")}");
