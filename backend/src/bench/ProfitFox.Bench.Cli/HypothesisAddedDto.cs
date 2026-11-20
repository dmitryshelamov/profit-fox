namespace ProfitFox.Bench.Cli;

/// <summary>
///     The answer of the bench API to a loaded hypothesis text.
/// </summary>
internal sealed record HypothesisAddedDto(string Id, string Name, int Version);
