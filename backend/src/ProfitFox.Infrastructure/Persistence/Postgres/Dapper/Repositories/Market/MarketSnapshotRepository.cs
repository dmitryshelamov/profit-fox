using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class MarketSnapshotRepository(NpgsqlDataSource dataSource) : IMarketSnapshotRepository
{
    public async Task<bool> ReplaceAsync(Guid owner, IReadOnlyCollection<MarketSnapshotRow> rows, DateTimeOffset snapshotAt, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteScalarAsync<int?>(MarketSnapshotRepositoryQueries.LockLease(owner, transaction, cancellationToken)) is null)
        {
            return false;
        }

        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.Clear(transaction, cancellationToken));
        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.Insert(rows.Select(MarketSnapshotRowDbConverter.FromDomain), transaction, cancellationToken));
        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.SnapshotAt(snapshotAt, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<MarketSnapshotRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<MarketSnapshotRowDb> rows = await connection.QueryAsync<MarketSnapshotRowDb>(MarketSnapshotRepositoryQueries.GetAll(cancellationToken));

        return [.. rows.Select(MarketSnapshotRowDbConverter.ToDomain)];
    }

    public async Task<MarketSnapshotRow?> GetAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        MarketSnapshotRowDb? row = await connection.QuerySingleOrDefaultAsync<MarketSnapshotRowDb>(
            MarketSnapshotRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, cancellationToken));

        return row is null ? null : MarketSnapshotRowDbConverter.ToDomain(row);
    }

    public async Task<bool> ReplaceSetupsAsync(Guid owner, IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteScalarAsync<int?>(MarketSnapshotRepositoryQueries.LockLease(owner, transaction, cancellationToken)) is null)
        {
            return false;
        }

        await connection.ExecuteAsync(MarketSnapshotRepositoryQueries.ClearSetups(transaction, cancellationToken));
        KeyValuePair<Instrument, IReadOnlyList<PairSetup>>[] held = [.. setups.Where(pair => pair.Value.Count > 0)];
        await using NpgsqlCommand command = MarketSnapshotRepositoryQueries.SetSetups(
            connection,
            transaction,
            [.. held.Select(pair => pair.Key.Category.ToCode())],
            [.. held.Select(pair => pair.Key.Symbol)],
            [.. held.Select(pair => Json(pair.Value))]);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>> GetSetupsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<(string Category, string Symbol, string Setups)> rows = await connection.QueryAsync<(string, string, string)>(
            MarketSnapshotRepositoryQueries.GetSetups(cancellationToken));

        return rows.ToDictionary(
            row =>
            {
                MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

                return new Instrument(category, row.Symbol);
            },
            row => Setups(row.Setups));
    }

    // {"15": ["impulse", "high_volatility"]}: the setups of each interval that has some.
    private static string Json(IReadOnlyList<PairSetup> setups)
    {
        return JsonSerializer.Serialize(
            setups.GroupBy(setup => setup.Interval).ToDictionary(group => group.Key.ToCode(), group => group.Select(setup => setup.Setup.ToCode()).ToArray()));
    }

    private static IReadOnlyList<PairSetup> Setups(string json)
    {
        return
        [
            .. JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)!.SelectMany(pair =>
            {
                MarketDataCodes.TryParseInterval(pair.Key, out Interval interval);

                return pair.Value.Select(code =>
                {
                    IdeaCodes.TryParseSetup(code, out IdeaSetup setup);

                    return new PairSetup(interval, setup);
                });
            })
        ];
    }
}