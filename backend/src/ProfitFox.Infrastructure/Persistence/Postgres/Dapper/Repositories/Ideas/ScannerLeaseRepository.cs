using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The lease of the scanner in the one row of <c>scanner_lease</c> (change add-ideas-and-market, design.md, decision 1):
///     taken, renewed and given up by one statement each on the time of the database.
/// </summary>
internal sealed class ScannerLeaseRepository(NpgsqlDataSource database) : IScannerLeaseRepository
{
    public async Task<bool> TakeAsync(string worker, TimeSpan duration, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        string? holder = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            ScannerLeaseRepositoryQueries.Take,
            new { ScannerLeaseRepositoryQueries.Name, Worker = worker, Duration = duration },
            cancellationToken: cancellationToken));
        return holder == worker;
    }

    public async Task ReleaseAsync(string worker, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(ScannerLeaseRepositoryQueries.Release, new { ScannerLeaseRepositoryQueries.Name, Worker = worker }, cancellationToken: cancellationToken));
    }

    public async Task ReportAsync(
        string worker,
        DateTimeOffset? liveSince,
        int warmed,
        int pairs,
        CancellationToken cancellationToken,
        int failed = 0,
        string? failure = null,
        TimeSpan? warmupLeft = null)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int? left = warmupLeft is { } time ? (int)Math.Min(Math.Ceiling(time.TotalSeconds), int.MaxValue) : null;
        await connection.ExecuteAsync(new CommandDefinition(
            ScannerLeaseRepositoryQueries.Report,
            new { ScannerLeaseRepositoryQueries.Name, Worker = worker, LiveSince = liveSince, Warmed = warmed, Pairs = pairs, Failed = failed, Failure = failure, WarmupLeft = left },
            cancellationToken: cancellationToken));
    }

    public async Task<(ScannerState State, DateTimeOffset Now)?> StateAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(ScannerLeaseRepositoryQueries.State, new { ScannerLeaseRepositoryQueries.Name }, cancellationToken: cancellationToken));
        return row is null ? null : (
            new ScannerState(
                row.Worker,
                row.Until,
                row.LiveSince,
                row.Warmed,
                row.Pairs,
                row.Failed,
                row.Failure,
                row.WarmupLeft is { } left ? TimeSpan.FromSeconds(left) : null),
            row.Now);
    }

    private sealed class Row
    {
        public string Worker { get; init; } = "";

        public DateTimeOffset Until { get; init; }

        public DateTimeOffset? LiveSince { get; init; }

        public int Warmed { get; init; }

        public int Pairs { get; init; }

        public int Failed { get; init; }

        public string? Failure { get; init; }

        public int? WarmupLeft { get; init; }

        public DateTimeOffset Now { get; init; }
    }
}
