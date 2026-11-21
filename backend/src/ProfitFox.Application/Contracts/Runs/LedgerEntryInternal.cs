namespace ProfitFox.Application.Contracts.Runs;

/// <summary>
///     A line of the ledger of runs (spec hypothesis-bench) as the API shows it: one run of one hypothesis on one period.
/// </summary>
public sealed record LedgerEntryInternal
{
    public required string RunId { get; init; }

    public required DateTimeOffset Time { get; init; }

    public required string Hypothesis { get; init; }

    public required string Hash { get; init; }

    /// <summary>
    ///     The version of the hypothesis the run was on.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    ///     The version of the engine the run was computed with.
    /// </summary>
    public required int EngineVersion { get; init; }

    public required string Period { get; init; }

    public required string Status { get; init; }

    public string? DataHash { get; init; }

    public int? Attempts { get; init; }

    public string? Reason { get; init; }

    public double? RequiredT { get; init; }

    public double? Power { get; init; }

    public int? Blocks { get; init; }

    public int? Events { get; init; }

    public double? T { get; init; }

    public double? Mean { get; init; }

    public double? FirstHalfMean { get; init; }

    public double? SecondHalfMean { get; init; }

    public double? ReadSeconds { get; init; }

    /// <summary>
    ///     The run this line corrects, when it is a correction.
    /// </summary>
    public string? Corrects { get; init; }
}
