namespace ProfitFox.Cli.Contracts.Runs;

/// <summary>
///     A run to put in the queue, as the bench API takes it.
/// </summary>
internal sealed record CreateRunDto(string Hypothesis, int? Version, string Period, string Kind);
