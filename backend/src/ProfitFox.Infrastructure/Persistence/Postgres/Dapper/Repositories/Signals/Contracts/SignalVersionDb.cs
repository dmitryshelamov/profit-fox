namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Contracts;

/// <summary>
///     A version a strategy points at: the signal's current name and latest version, and the graph of the version, null when
///     there is no such version.
/// </summary>
internal sealed record SignalVersionDb
{
    public Guid Signal { get; init; }

    public int Version { get; init; }

    public string Name { get; init; } = "";

    public int LatestVersion { get; init; }

    public string? Graph { get; init; }
}
