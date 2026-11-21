namespace ProfitFox.Cli.Runs;

/// <summary>
///     The line of the ledger of a run that ended, as the bench API tells it.
/// </summary>
internal sealed record RunResultDto(string Status, string? Reason, double? T, double? RequiredT, double? Power);
