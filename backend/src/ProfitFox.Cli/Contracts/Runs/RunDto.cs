namespace ProfitFox.Cli.Contracts.Runs;

/// <summary>
///     A run as the bench API tells it: only the fields the commands print.
/// </summary>
internal sealed record RunDto(
    string Id,
    string Hypothesis,
    int Version,
    string Period,
    string Kind,
    string State,
    string? Worker,
    int PairsDone,
    int? PairsTotal,
    RunResultDto? Result);
