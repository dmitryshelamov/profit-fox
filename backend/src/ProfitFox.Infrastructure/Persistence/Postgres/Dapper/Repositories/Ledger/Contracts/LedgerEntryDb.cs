namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;

/// <summary>
///     A row of <c>ledger</c>.
/// </summary>
internal record LedgerEntryDb
{
    public string RunId { get; init; } = "";

    public DateTimeOffset Time { get; init; }

    public string Hypothesis { get; init; } = "";

    public string Hash { get; init; } = "";

    public int Version { get; init; }

    public int EngineVersion { get; init; }

    public string Period { get; init; } = "";

    public string Status { get; init; } = "";

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

    public string? Corrects { get; init; }
}
