using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class ScannerStateRepository(NpgsqlDataSource dataSource) : IScannerStateRepository
{
    public async Task<bool> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(ScannerStateRepositoryQueries.Claim(owner, now, leasedUntil, cancellationToken)) > 0;
    }

    public async Task<bool> RenewAsync(Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(ScannerStateRepositoryQueries.Renew(owner, leasedUntil, cancellationToken)) > 0;
    }

    public async Task PauseRestAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(ScannerStateRepositoryQueries.PauseRest(until, cancellationToken));
    }

    public async Task<ScannerState> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        ScannerStateDb state = await connection.QuerySingleAsync<ScannerStateDb>(ScannerStateRepositoryQueries.Get(cancellationToken));

        return new ScannerState(state.SnapshotAt, state.PairsTotal, state.PairsBackfilled, state.RestPausedUntil);
    }

    public async Task SetProgressAsync(int pairsTotal, int pairsBackfilled, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(ScannerStateRepositoryQueries.SetProgress(pairsTotal, pairsBackfilled, cancellationToken));
    }
}