namespace ProfitFox.Bench.Presentation.Runs;

/// <summary>
///     The line of the ledger of a run that ended, and its report in markdown when the run has one and the answer is of a single
///     run (the list of runs leaves the reports out). <see cref="Status" /> is <c>passed</c>, <c>rejected</c>,
///     <c>insufficient_data</c>, <c>parity</c>, <c>failed</c> or <c>cancelled</c>.
/// </summary>
public sealed record RunResultResponse(
    DateTimeOffset Time,
    string Status,
    string? Reason,
    string Hash,
    int Version,
    int EngineVersion,
    string? DataHash,
    int? Attempts,
    double? RequiredT,
    double? Power,
    int? Blocks,
    int? Events,
    double? T,
    double? Mean,
    double? FirstHalfMean,
    double? SecondHalfMean,
    double? ReadSeconds,
    string? Corrects,
    string? Report);
